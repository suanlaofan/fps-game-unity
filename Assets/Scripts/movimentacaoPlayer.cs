using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class movimentacaoPlayer : MonoBehaviour
{
    public Camera mainCamera;
    public float sensibilidade = 50;

    private float rotacaoX = 0f;
    private float rotaçãoY = 0f;
    private float anguloLimite = 80.0f;

    // Start is called before the first frame update
    void Start()
    {
        Vector3 rotacao = transform.localRotation.eulerAngles;
        rotaçãoY = rotacao.y;
        rotacaoX = rotacao.x;
    }

    // Update is called once per frame
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Escape)){
            Cursor.lockState = CursorLockMode.None;
        } 
        Cursor.lockState = CursorLockMode.Locked;

        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");
        rotaçãoY += mouseX * sensibilidade * Time.deltaTime;
        rotacaoX += mouseY * sensibilidade * Time.deltaTime;
        rotacaoX  = Mathf.Clamp(rotacaoX,-anguloLimite,anguloLimite); 
        Quaternion rotacaoLocal = Quaternion.Euler(rotacaoX * (-1),rotaçãoY,0f);
        transform.rotation = rotacaoLocal;
    }
}
