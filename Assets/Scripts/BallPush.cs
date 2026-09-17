using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BallPush : MonoBehaviour
{
    void Awake()
    {
        var rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
    }
}