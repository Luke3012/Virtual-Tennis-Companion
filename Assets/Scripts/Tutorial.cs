using I2.TextAnimation;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Tutorial : MonoBehaviour
{
    public TMP_Text descr1Text;
    public TMP_Text nextButtonText;
    public Button nextButton;
    public GameObject luca;
    public GameObject lucrezia;
    public GameObject racchetta;

    bool isSecondaParte = false;

    void Start()
    {
        // Inizializza le posizioni degli oggetti fuori dallo schermo
        luca.transform.position += new Vector3(10, 0, 0); // Sposta luca a sinistra
        lucrezia.transform.position -= new Vector3(10, 0, 0); // Sposta lucrezia a destra
        nextButton.transform.position -= new Vector3(0, 10, 0); // Sposta il bottone in basso
        racchetta.transform.position -= new Vector3(0, 10, 0); // Sposta la racchetta in basso
        StartCoroutine(PlayTutorialSequence());

        nextButton.onClick.AddListener(() =>
        {
            if (!isSecondaParte)
            {
                isSecondaParte = true;
                StartCoroutine(PlaySecondPartSequence());
                nextButtonText.text = "Inizia";
                return;
            }


            SceneManager.LoadScene("ScenaPrincipale");
        });
    }

    IEnumerator PlayTutorialSequence()
    {
        // Anima il testo
        descr1Text.GetComponent<TextAnimation>().PlayAnim(0);

        // Anima luca e lucrezia
        StartCoroutine(MoveFromRight(luca));
        yield return StartCoroutine(MoveFromLeft(lucrezia));

        // Inizia la rotazione
        StartCoroutine(RotateObject(luca));
        StartCoroutine(RotateObject(lucrezia));

        // Anima il bottone
        yield return StartCoroutine(MoveFromBottom(nextButton.gameObject));
    }

    IEnumerator PlaySecondPartSequence()
    {
        // Cambia il testo e riproduce l'animazione
        descr1Text.text = "Se necessario premi sulla racchetta per effettuare la calibrazione, ma ricordati sempre di posizionarla davanti a te.";
        descr1Text.GetComponent<TextAnimation>().PlayAnim(1);

        // Anima l'uscita di luca e lucrezia
        StartCoroutine(MoveFromLeft(luca));
        yield return StartCoroutine(MoveFromRight(lucrezia));

        // Anima l'ingresso della racchetta
        yield return StartCoroutine(MoveFromBottom(racchetta));
        StartCoroutine(RotateObject(racchetta));
    }

    IEnumerator MoveFromLeft(GameObject obj)
    {
        Vector3 startPos = obj.transform.position;
        Vector3 endPos = startPos + new Vector3(10, 0, 0); // Posizione finale
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

    IEnumerator MoveFromRight(GameObject obj)
    {
        Vector3 startPos = obj.transform.position;
        Vector3 endPos = startPos - new Vector3(10, 0, 0); // Posizione finale
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
        while (true)
        {
            obj.transform.Rotate(new Vector3(0, 45, 0) * Time.deltaTime); // Velocità di rotazione
            yield return null;
        }
    }
}
