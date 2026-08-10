using UnityEngine;

public class Rotate : MonoBehaviour
{
    [SerializeField, Tooltip("Tốc độ xoay quanh trục Y, tính bằng độ/giây.")]
    private float rotationSpeedY = 90f;

    private void Update()
    {
        transform.Rotate(0f, rotationSpeedY * Time.deltaTime, 0f, Space.Self);
    }
}
