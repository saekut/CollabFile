/*
 UNITY UDP RECEIVER  (Phase 2/3) - multiple props
 Listens for the detection message your Python script sends and lists EVERY
 prop found in the zone, each tinted with its category colour:

     Detected (2):
     Bro?   [GREEN]
     Test   [ORANGE]

 SETUP: attach to your Receiver object, drag ONE text into "Display Text",
 keep port = 5005 (same as Python). (Rich Text must stay enabled on the text.)
*/

using UnityEngine;
using TMPro;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

public class UnityUdpReceiver : MonoBehaviour
{
    public int port = 5005;
    public TMP_Text displayText;

    private UdpClient client;
    private Thread receiveThread;
    private volatile bool running = false;
    private readonly Queue<string> queue = new Queue<string>();
    private readonly object locker = new object();

    [System.Serializable]
    public class Item
    {
        public string name;
        public string category;
    }

    [System.Serializable]
    public class Detection
    {
        public string status;
        public int count;
        public Item[] items;
    }

    void Start()
    {
        ShowEmpty();
        StartReceiver();
    }

    void StartReceiver()
    {
        try
        {
            client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket,
                                          SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        }
        catch (System.Exception e)
        {
            Debug.LogError("UDP bind FAILED on port " + port + ": " + e.Message);
            return;
        }

        running = true;
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();
        Debug.Log("UDP receiver listening on port " + port);
    }

    void ReceiveLoop()
    {
        IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = client.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data);
                lock (locker) { queue.Enqueue(text); }
            }
            catch (System.Exception)
            {
                break;
            }
        }
    }

    void Update()
    {
        string msg = null;
        lock (locker)
        {
            while (queue.Count > 0) msg = queue.Dequeue();  // keep newest only
        }
        if (msg == null) return;

        Detection d = JsonUtility.FromJson<Detection>(msg);
        if (d == null || displayText == null) return;

        if (d.status == "ok" && d.items != null && d.items.Length > 0)
        {
            displayText.color = Color.white;
            var sb = new StringBuilder();
            sb.Append("Detected (").Append(d.items.Length).Append("):\n");
            foreach (var it in d.items)
            {
                string hex = HexFor(it.category);
                sb.Append("<color=#").Append(hex).Append(">")
                  .Append(it.name).Append("   [").Append(it.category.ToUpper()).Append("]")
                  .Append("</color>\n");
            }
            displayText.text = sb.ToString();
        }
        else
        {
            ShowEmpty();
        }
    }

    void ShowEmpty()
    {
        if (displayText == null) return;
        displayText.color = Color.white;
        displayText.text = "Show props...";
    }

    string HexFor(string category)
    {
        switch (category)
        {
            case "orange": return "FF8C00";
            case "yellow": return "FFD400";
            case "green":  return "33CC33";
            case "white":  return "FFFFFF";
        }
        return "FFFFFF";
    }

    void StopReceiver()
    {
        running = false;
        if (client != null) { client.Close(); client = null; }
        if (receiveThread != null) { receiveThread.Join(200); receiveThread = null; }
    }

    void OnDisable() { StopReceiver(); }
    void OnApplicationQuit() { StopReceiver(); }
}
