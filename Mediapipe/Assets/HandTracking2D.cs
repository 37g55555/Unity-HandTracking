using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandTracking2D : MonoBehaviour
{
    public UDPReceive udpReceive;
    public GameObject[] handPoints1;
    public GameObject[] handPoints2;
    public float offset = 200f;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        string data = udpReceive.data;
        data = data.Remove(0, 1);
        data = data.Remove(data.Length-1, 1);
        string[] points = data.Split(',');

        if (points.Length >= 63)
        {
            for ( int i = 0; i < 21; i++)
            {
                float x = Screen.width - float.Parse(points[i * 3]) - offset;
                float y = float.Parse(points[i * 3 + 1]);
                //float z = float.Parse(points[i * 3 + 2]);

                Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(x, y, 10));
                handPoints1[i].transform.position = new Vector3(worldPos.x, worldPos.y, 0);
            }
        }

        if (points.Length >= 126)
        {
            for ( int i = 0; i < 21; i++)
            {
                float x = Screen.width - float.Parse(points[(i + 21) * 3]) - offset;
                float y = float.Parse(points[(i + 21) * 3 + 1]);
                //float z = float.Parse(points[(i + 21) * 3 + 2]);
    
                Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(x, y, 10));
                handPoints2[i].transform.position = new Vector3(worldPos.x, worldPos.y, 0);
            }
        }
    }
}