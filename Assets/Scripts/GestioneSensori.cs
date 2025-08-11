using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System;
using System.IO;
using TMPro;
using I2.TextAnimation;

public class GestioneSensori : MonoBehaviour
{
    public TMP_Text titleText;
    public Button startButton;
    public TMP_Text startButtonText;
    public Button calibrateButton;

    public GameObject controlPanel;
    public Button sxButton;
    public Button okButton;
    public Button dxButton;

    public GameObject resumePanel;
    public Button resumeButton;
    public Button exitButton;

    public GameObject pausePanel;
    public Button pauseButton;
    public Button firstPersonButton;
    public Button musicButton;
    public TMP_Text firstPersonText;
    public TMP_Text musicText;

    public GameObject confirmPanel;
    public Button confirmButton;

    public TMP_Text numCountText;

    private GameObject racchetta;
    private GameObject palla;

    private TcpClient client;
    private NetworkStream stream;
    private UdpClient udpClient;
    private IPEndPoint serverEndPoint;

    // Variabile per il giroscopio
    private Gyroscope gyro;
    private bool gyroEnabled;
    private bool isConnected;

    private Quaternion calibrationOffset = Quaternion.identity;
    private Quaternion initialGyroRotation;
    private bool isCalibrated = false;
    private bool isCalibrating = false;
    private bool isTcpError;

    // Coda per azioni da eseguire nel thread principale
    private Queue<Action> mainThreadActions = new Queue<Action>();

    // Variabili per salvare e ripristinare la rotazione della racchetta
    private Quaternion savedRacchettaRotation;
    private Vector3 savedRacchettaPosition;
    private bool isRestoringRotation = false;
    private float restoreDuration = 1.0f;  // Durata dell'animazione di ripristino

    private bool isLucrezia = false;
    

    void Start()
    {
        startButton.enabled = true;
        startButton.gameObject.SetActive(true);
        isConnected = false;
        isTcpError = false;

        racchetta = GameObject.FindGameObjectWithTag("Racchetta");
        palla = GameObject.FindGameObjectWithTag("Palla");

        savedRacchettaRotation = racchetta.transform.rotation;
        savedRacchettaPosition = racchetta.transform.position;

        ResetPanels();

        startButton.onClick.AddListener(() =>
        {
            DiscoverServer();
            Vibrate(0.1f);
        });
        calibrateButton.onClick.AddListener(() =>
        {
            if ((isConnected && gyroEnabled) || testMode)
            {
                CalibrateRacchetta();
                Vibrate(0.2f);
            }
        });
        sxButton.onClick.AddListener(() =>
        {
            SendData("SX");
            Vibrate(0.1f);
        });
        okButton.onClick.AddListener(() =>
        {
            SendData("OK");
            Vibrate(0.1f);
        });
        dxButton.onClick.AddListener(() =>
        {
            SendData("DX");
            Vibrate(0.1f);
        });
        resumeButton.onClick.AddListener(() =>
        {
            SendData("RESUME");
            Vibrate(0.1f);
            RipristinaSensori();
            isSwingData = true;
        });
        exitButton.onClick.AddListener(() =>
        {
            SendData("EXIT");
            Vibrate(0.1f);
        });
        pauseButton.onClick.AddListener(() =>
        {
            SendData("PAUSE");
            Vibrate(0.1f);
        });
        firstPersonButton.onClick.AddListener(() =>
        {
            if (firstPersonText.text == "fp off")
                firstPersonText.text = "fp on";
            else
                firstPersonText.text = "fp off";
            SendData("PRIMAPERSONA");
            Vibrate(0.1f);
        });
        musicButton.onClick.AddListener(() =>
        {
            if (musicText.text == "sound on")
                musicText.text = "sound off";
            else
                musicText.text = "sound on";
            SendData("MUSIC");
            Vibrate(0.1f);
        });
        confirmButton.onClick.AddListener(() =>
        {
            Vibrate(0.1f);

            if (!racchetta.activeSelf)
            {
                // in questo caso sta mostrando la vittoria, per qui bisogna fare azioni per nascondere tutto
                racchetta.SetActive(true);
                SendData("CONFIRM");
            } else if (!isCalibrated) {
                // in questo caso deve fare la calibrazione prima di proseguire
                CalibrateRacchetta();
            }
        });

        gyroEnabled = EnableGyroscope();
    }

    void Vibrate(float duration = 0.1f)
    {
        Handheld.Vibrate();
        StartCoroutine(VibrationCoroutine(duration));
    }

    IEnumerator VibrationCoroutine(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Handheld.Vibrate();
    }


    bool testMode = false;
    bool isSwingData = false;
    private Vector3 prevGyroData;
    private float swingThresholdStrong = 350.0f; // Soglia per colpo forte
    private float swingThresholdWeak = 300.0f;  // Soglia per colpo debole
    private bool isSwingDetected = false;
    private bool canDetectSwing = true; // Flag per swing
    float swingCooldown = 0.2f; // Tempo di attesa tra swing
    private float angularVelocityThreshold = 100.0f; // Soglia minima per considerare uno swing
    private float minSwingDistance = 30.0f; // Distanza angolare minima per swing
    private Vector3 startSwingRotation; // Salva l'inizio dello swing
    private bool isSwingInProgress = false; // Flag per tracciare swing in corso

    private bool isServing = false; // Flag per la fase di preparazione della battuta
    private float servePreparationThreshold = 100.0f; // Soglia per movimento laterale durante la preparazione
    private float serveSwingThreshold = 280.0f; // Soglia per il movimento che rappresenta la battuta effettiva

    void Update()
    {
        // Processa azioni in coda dal thread principale
        lock (mainThreadActions)
        {
            while (mainThreadActions.Count > 0)
            {
                mainThreadActions.Dequeue().Invoke();
            }
        }

        // Verifica lo stato della connessione
        CheckConnectionStatus();


        if ((gyroEnabled && isCalibrated && !isRestoringRotation && racchetta.activeSelf) || testMode)
        {
            // Ottieni la rotazione del giroscopio
            Quaternion gyroRotation = gyro.attitude;
            Quaternion deviceRotation = new Quaternion(gyroRotation.x, gyroRotation.y, -gyroRotation.z, -gyroRotation.w);
            Quaternion finalRotation = calibrationOffset * deviceRotation;
            racchetta.transform.rotation = finalRotation;

            if (isSwingData)
            {
                // Ottieni i dati correnti di rotazione
                Vector3 currentGyroData = finalRotation.eulerAngles;

                // Calcola la differenza di rotazione rispetto al frame precedente
                Vector3 deltaRotation = currentGyroData - prevGyroData;

                // Calcola la velocità angolare (variazione dell'angolo rispetto al tempo)
                float angularVelocity = deltaRotation.magnitude / Time.deltaTime;

                // Se siamo in fase di battuta, filtra i movimenti preparatori
                if (isServing)
                {
                    // Controlla se il movimento è principalmente laterale (asse Y)
                    if (Mathf.Abs(deltaRotation.y) > servePreparationThreshold && Mathf.Abs(deltaRotation.x) < serveSwingThreshold)
                    {
                        // Movimento laterale rilevato, ignora lo swing per ora
                        Debug.Log("Fase di preparazione della battuta. Ignorando swing...");
                        return;
                    }
                    if (Mathf.Abs(deltaRotation.x) > serveSwingThreshold) // Movimento che indica la battuta
                    {
                        // Fine della preparazione, rileva lo swing
                        Debug.Log("Movimento di battuta rilevato.");
                        isServing = false; // Esce dalla fase di preparazione
                    }
                }

                // Se supera la soglia di velocità angolare minima, inizia a tracciare uno swing
                if (angularVelocity > angularVelocityThreshold && canDetectSwing)
                {
                    if (!isSwingInProgress)
                    {
                        // Inizio dello swing, salva la rotazione iniziale
                        startSwingRotation = currentGyroData;
                        isSwingInProgress = true;
                    }
                }

                // Se uno swing è in corso, controlla la distanza percorsa
                if (isSwingInProgress)
                {
                    // Calcola la distanza angolare percorsa dallo swing
                    float swingDistance = Vector3.Distance(currentGyroData, startSwingRotation);

                    // Se la distanza percorsa supera la soglia minima e possiamo rilevare swing
                    if (swingDistance > minSwingDistance && canDetectSwing)
                    {
                        if (swingDistance > swingThresholdStrong)
                        {
                            // Swing forte rilevato
                            isSwingDetected = true;
                            Debug.Log("Swing Forte rilevato! " + swingDistance);
                            //StartCoroutine(ShowMessage("Swing Forte rilevato!", 1f));
                            //Vibrate(0.1f);
                            SendData("SWING_FORTE");
                            StartCoroutine(SwingCooldown());
                        }
                        else if (swingDistance > swingThresholdWeak)
                        {
                            // Swing debole rilevato
                            isSwingDetected = true;
                            Debug.Log("Swing Debole rilevato!" + swingDistance);
                            //StartCoroutine(ShowMessage("Swing Debole rilevato!", 1f));
                            //Vibrate(0.01f);
                            SendData("SWING_DEBOLE");
                            StartCoroutine(SwingCooldown());
                        }

                        // Fine dello swing
                        isSwingInProgress = false;
                    }
                }

                // Aggiorna la rotazione precedente
                prevGyroData = currentGyroData;
            }
        }
    }

    IEnumerator SwingCooldown()
    {
        canDetectSwing = false;
        yield return new WaitForSeconds(swingCooldown);
        canDetectSwing = true;
    }




    private bool EnableGyroscope()
    {
        if (SystemInfo.supportsGyroscope)
        {
            gyro = Input.gyro;
            gyro.enabled = true;
            return true;
        }
        return false;
    }

    void DiscoverServer()
    {
        startButton.enabled = false;
        startButtonText.text = "Connessione...";
        udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, 5001);

        byte[] data = Encoding.ASCII.GetBytes("DISCOVER_SERVER");
        udpClient.Send(data, data.Length, endPoint);
        Debug.Log("Messaggio di broadcast inviato...");

        Thread receiveThread = new Thread(new ThreadStart(ReceiveServerResponse));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        // Attendi 5 secondi prima di riattivare il pulsante di avvio
        StartCoroutine(EnableStartButtonAfterDelay(5f));
    }

    IEnumerator EnableStartButtonAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        startButton.enabled = true;
        startButtonText.text = "Connetti";
    }

    void ReceiveServerResponse()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        udpClient.Client.ReceiveTimeout = 5000;  // Imposta un timeout per la ricezione
        try
        {
            byte[] data = udpClient.Receive(ref remoteEndPoint);
            string message = Encoding.ASCII.GetString(data);

            if (message == "SERVER_RESPONSE")
            {
                serverEndPoint = remoteEndPoint;
                Debug.Log("Virtual Tennis+ scoperto in: " + serverEndPoint.Address.ToString());
                ConnectToServer(serverEndPoint.Address.ToString());
            }
        }
        catch (SocketException ex)
        {
            Debug.LogError("Nessuna risposta dal server: " + ex.Message);
            lock (mainThreadActions)
            {
                mainThreadActions.Enqueue(() =>
                {
                    StartCoroutine(ShowMessage("Nessuna istanza trovata.", 3f));
                });
            }
        }
    }

    void ConnectToServer(string serverAddress)
    {
        client = new TcpClient();
        client.Connect(serverAddress, 5000);
        stream = client.GetStream();
        Debug.Log("Connesso al server");

        // Invia un messaggio di esempio al server
        SendData("Pronto? Qui risponde Virtual Tennis+ Companion all'indirizzo " + client.Client.LocalEndPoint.ToString());
        
        mainThreadActions.Enqueue(() =>
        {
            startButton.gameObject.SetActive(false);
            // Fai vibrare lo smartphone per 0.2 secondi
            Vibrate(0.2f);

            isConnected = true;
        });

        // Avvia un thread per ascoltare i messaggi dal server
        Thread receiveThread = new Thread(new ThreadStart(ReceiveTcpMessages));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        // Avvia la Coroutine per inviare il ping ogni 5 secondi
        StartCoroutine(SendPingEvery5Seconds());
    }


    void ReceiveTcpMessages()
    {
        byte[] buffer = new byte[1024];
        int bytesRead;

        try
        {
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                Debug.Log("Ricevuto dal server: " + message);

                // Invia l'azione al thread principale tramite la coda
                lock (mainThreadActions)
                {
                    mainThreadActions.Enqueue(() =>
                    {
                        if (message == "PAUSE")
                        {
                            ResetPanels();
                            isSwingData = true;
                            pausePanel.SetActive(true);
                            currentPanel = pausePanel;
                        }
                        else if (message == "RESUME")
                        {
                            ResetPanels();
                            resumePanel.SetActive(true);
                            currentPanel = resumePanel;
                        } else if (message == "CALIBRATE")
                        {
                            ResetPanels();
                            confirmPanel.SetActive(true);
                            currentPanel = confirmPanel;
                        }
                        else if (message == "CONTROL" || message == "EXIT")
                        {
                            ResetPanels();
                            controlPanel.SetActive(true);
                            currentPanel = controlPanel;

                            if (!racchetta.activeSelf)
                            {
                                racchetta.SetActive(true);
                            }
                        } else if (message.Contains("ANIM") && racchetta.activeSelf)
                        {
                            ResetPanels();
                            mainThreadActions.Enqueue(() =>
                            {
                                StartCoroutine(AnimazioneVittoria(message.Replace("ANIM", "")));
                            });
                        } else if (message == "LUCREZIA")
                        {
                            isLucrezia = true;
                        } else if (message == "LUCA")
                        {
                            isLucrezia = false;
                        } else if (message == "BATTUTA")
                        {
                            isServing = true;
                            isSwingData = true;
                        } else if (message == "SWING")
                        {
                            isServing = false;
                            isSwingData = true;
                        } else if (message == "SOUNDON")
                            musicText.text = "sound on";
                        else if (message == "SOUNDOFF")
                            musicText.text = "sound off";
                        else if (message == "FPON")
                            firstPersonText.text = "fp on";
                        else if (message == "FPOFF")
                            firstPersonText.text = "fp off";
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Errore nella ricezione dei messaggi TCP: " + ex.Message);
            isTcpError = true;
        }
    }


    private Coroutine currentMessageCoroutine = null;
    IEnumerator ShowMessage(string message, float seconds = 3f, string restore = null)
    {
        // Interrompi la coroutine attuale se esiste
        if (currentMessageCoroutine != null)
        {
            StopCoroutine(currentMessageCoroutine);
        }

        // Avvia una nuova coroutine per il messaggio
        currentMessageCoroutine = StartCoroutine(DisplayMessageCoroutine(message, seconds, restore));
        yield return null;
    }

    IEnumerator DisplayMessageCoroutine(string message, float seconds, string restore)
    {
        if (restore == null) restore = titleText.text;

        // Mostra il nuovo messaggio
        titleText.text = message;
        titleText.GetComponent<TextAnimation>().PlayAnim(0);

        yield return new WaitForSeconds(seconds);

        // Ripristina il testo originale
        titleText.text = restore;

        currentMessageCoroutine = null;  // Resetta la coroutine corrente
    }



    private GameObject currentPanel = null; // Variabile per tenere traccia del pannello attivo

    void ResetPanels()
    {
        controlPanel.SetActive(false);
        resumePanel.SetActive(false);
        pausePanel.SetActive(false);
        confirmPanel.SetActive(false);
        numCountText.gameObject.SetActive(false);
        racchetta.SetActive(true);
        isSwingData = false; // Fa in modo che non registra gli swing
        currentPanel = null;  // Reset del pannello attivo
    }

    // Funzione per nascondere e ripristinare il pannello attivo durante la calibrazione
    void HideCurrentPanel()
    {
        if (controlPanel.activeSelf)
        {
            controlPanel.SetActive(false);
            currentPanel = controlPanel;
        }
        else if (resumePanel.activeSelf)
        {
            resumePanel.SetActive(false);
            currentPanel = resumePanel;
        }
        else if (pausePanel.activeSelf)
        {
            pausePanel.SetActive(false);
            currentPanel = pausePanel;
        }
        else if (confirmPanel.activeSelf)
        {
            confirmPanel.SetActive(false);
            currentPanel = confirmPanel;
        } else if (calibrateButton.gameObject.activeSelf)
        {
            calibrateButton.gameObject.SetActive(false);
            currentPanel = calibrateButton.gameObject;
        }
    }

    void ShowCurrentPanel()
    {
        if (currentPanel != null)
        {
            currentPanel.SetActive(true);
        }
    }

    // Nascondi il pannello all'inizio della calibrazione
    void CalibrateRacchetta()
    {
        HideCurrentPanel();  // Nascondi il pannello attualmente visibile
        isCalibrating = true;
        StartCoroutine(ShowMessage("Calibrazione in corso...", 3f, "Virtual Tennis+ Companion"));
        StartCoroutine(CountdownAndCalibrate());
    }

    // Mostra il pannello attivo dopo la calibrazione
    IEnumerator CountdownAndCalibrate()
    {
        Vector3 startPosition = racchetta.transform.position;
        Quaternion startRotation = racchetta.transform.rotation;

        for (int i = 3; i > 0; i--)
        {
            numCountText.gameObject.SetActive(true);
            numCountText.text = i.ToString();
            yield return new WaitForSeconds(1f);

            // Verifica se la racchetta si è mossa
            if (Vector3.Distance(racchetta.transform.position, startPosition) > 0.05f ||
                Quaternion.Angle(racchetta.transform.rotation, startRotation) > 5f)
            {
                numCountText.gameObject.SetActive(false);
                StartCoroutine(ShowMessage("Errore calibrazione!", 3f, "Virtual Tennis+ Companion"));
                isCalibrating = false;
                ShowCurrentPanel();  // Ripristina il pannello attivo

                if (!isCalibrated)  // Se questa è stata la prima calibrazione, invia ERRORE al Server
                {
                    DisconnectFromServer();
                    SendData("ERRORE");
                }

                yield break;
            }
        }

        numCountText.gameObject.SetActive(false);
        StartCoroutine(ShowMessage("Calibrazione completata!", 3f, "Virtual Tennis+ Companion"));
        racchetta.transform.position = new Vector3(694.75f, 150.37f, -769.74f);
        racchetta.transform.rotation = Quaternion.Euler(4.48f, 269.47f, 275.90f);

        initialGyroRotation = gyro.attitude;
        Quaternion deviceRotation = new Quaternion(initialGyroRotation.x, initialGyroRotation.y, -initialGyroRotation.z, -initialGyroRotation.w);
        calibrationOffset = racchetta.transform.rotation * Quaternion.Inverse(deviceRotation);

        if (!isCalibrated) //Se questa è stata la prima calibrazione, invia CALIBRATED al Server
        {
            SendData("CALIBRATED");
        }

        isCalibrating = false;
        isCalibrated = true;

        // Ripristina il pannello attivo
        ShowCurrentPanel();
    }



    public void SendData(string message)
    {
        if (stream != null && stream.CanWrite)
        {
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(message);
                stream.Write(data, 0, data.Length);
                Debug.Log("Invio al Gioco: " + message);
            }
            catch (IOException ex)
            {
                Debug.LogError("Errore nell'invio dei dati: " + ex.Message);
                isTcpError = true;
            }
        }
    }


    void RipristinaSensori()
    {
        isSwingDetected = false;
        canDetectSwing = true;
        isSwingInProgress = false;
        isServing = false;
        isSwingData = false;
    }


    void OnApplicationQuit()
    {
        DisconnectFromServer();
    }

    void DisconnectFromServer()
    {
        if (stream != null)
        {
            stream.Close();
        }

        if (client != null)
        {
            client.Close();
        }

        if (udpClient != null)
        {
            udpClient.Close();
        }

        ResetPanels();
        RipristinaSensori();
        isTcpError = false;
        isConnected = false;
        isCalibrated = false;

        // Riattiva il pulsante startButton
        startButton.gameObject.SetActive(true);
        startButton.enabled = true;

        // Inizia il ripristino graduale della rotazione salvata
        StartCoroutine(RestoreRacchettaRotation());
    }

    void CheckConnectionStatus()
    {
        if (isConnected && isTcpError)
        {
            Debug.Log("Connessione persa. Riattivazione del pulsante startButton.");
            DisconnectFromServer();
        }
    }

    IEnumerator SendPingEvery5Seconds()
    {
        while (isConnected)  // Continua finché il dispositivo è connesso
        {
            SendData("PING");  // Invia il messaggio di ping
            yield return new WaitForSeconds(5f);  // Attendi 5 secondi
        }
    }
    
    IEnumerator RestoreRacchettaRotation()
    {
        isRestoringRotation = true;
        Quaternion currentRotation = racchetta.transform.rotation;
        Vector3 currentPosition = racchetta.transform.position;  // Salva la posizione attuale

        float elapsedTime = 0f;

        while (elapsedTime < restoreDuration)
        {
            // Interpola la rotazione
            racchetta.transform.rotation = Quaternion.Slerp(currentRotation, savedRacchettaRotation, elapsedTime / restoreDuration);

            // Interpola la posizione
            racchetta.transform.position = Vector3.Lerp(currentPosition, savedRacchettaPosition, elapsedTime / restoreDuration);

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // Assicurati che la posizione e la rotazione siano esatte alla fine
        racchetta.transform.rotation = savedRacchettaRotation;
        racchetta.transform.position = savedRacchettaPosition;

        isRestoringRotation = false;
    }



    IEnumerator AnimazioneVittoria (string anim) //ANIMBRONZO - ANIMARGENTO - ANIMORO - ANIMVITTORIA - ANIMSCONFITTA
    {
        racchetta.SetActive(false);
        GameObject daMostrare = null;
        string testoTitle = "Congratulazioni! Hai vinto!";

        if (anim.Contains("BRONZO"))
        {
            daMostrare = Instantiate(Resources.Load<GameObject>("CoppaBronzo"));
        }
        else if (anim.Contains("ARGENTO"))
        {
            daMostrare = Instantiate(Resources.Load<GameObject>("CoppaArgento"));
        }
        else if (anim.Contains("ORO"))
        {
            daMostrare = Instantiate(Resources.Load<GameObject>("CoppaOro"));
        }
        else if (anim.Contains("VITTORIA") || anim.Contains("SCONFITTA"))
        {
            if (isLucrezia)
            {
                daMostrare = Instantiate(Resources.Load<GameObject>("Lucrezia"));
            }
            else
            {
                daMostrare = Instantiate(Resources.Load<GameObject>("Luca"));
            }

            if (anim.Contains("VITTORIA"))
            {
                //daMostrare.GetComponent<Animator>().SetTrigger("Vittoria");
            }
            else
            {
                testoTitle = "Hai perso! Ritenta!";
                //daMostrare.GetComponent<Animator>().SetTrigger("Sconfitta");
            }
        }
        ShowMessage(testoTitle, 3f);

        daMostrare.transform.position = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width / 2, Screen.height / 2 - 80f, 10f));
        CreaLuceDirezionale(daMostrare);
        daMostrare.transform.position -= new Vector3(0, 10, 0);
        StartCoroutine(RotateObject(daMostrare));
        yield return StartCoroutine(MoveFromBottom(daMostrare.gameObject)); //Attende l'animazione

        confirmPanel.SetActive(true);

        while (!racchetta.activeSelf)
        {
            titleText.text = testoTitle;
            yield return null;
        }
        
        daMostrare.SetActive(false);
        Destroy(daMostrare);
        titleText.text = "Virtual Tennis+ Companion";
    }

    void CreaLuceDirezionale(GameObject target)
    {
        GameObject luceObj = new GameObject("LuceDirezionale");
        Light luce = luceObj.AddComponent<Light>();
        luce.type = LightType.Point;
        luce.color = Color.white;
        luce.intensity = 5.0f;

        // Posiziona la luce in modo che illumini l'oggetto
        luceObj.transform.position = target.transform.position + new Vector3(5, 1, -7);
        luceObj.transform.LookAt(target.transform);
    }

    IEnumerator MoveFromBottom(GameObject obj)
    {
        Vector3 startPos = obj.transform.position;
        Vector3 endPos = startPos + new Vector3(0, 10, 0); // Posizione finale
        float duration = 2.0f; // Durata dell'animazione
        float elapsed = 0;

        while (elapsed < duration)
        {
            obj.transform.position = Vector3.Lerp(startPos, endPos, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        obj.transform.position = endPos;
    }

    IEnumerator RotateObject(GameObject obj)
    {
        while (!racchetta.activeSelf)
        {
            obj.transform.Rotate(new Vector3(0, 45, 0) * Time.deltaTime); // Velocità di rotazione
            yield return null;
        }
    }
}
