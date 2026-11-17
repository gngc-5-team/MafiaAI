using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.Mystery.UI
{
    /// <summary>원고·쪽지를 종이 한 장으로 펼쳐 읽는다(노아의 문장 — 이야기가 흘러들어오는 창).</summary>
    public class ReaderView : MonoBehaviour
    {
        public GameObject root;
        public TMP_Text title;
        public TMP_Text body;
        public TMP_Text footer;
        public Button closeButton;

        public event Action Closed;

        void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        public bool Open => root != null && root.activeSelf;

        public void Show(string heading, string text, string note = null)
        {
            if (root != null) root.SetActive(true);
            if (title != null) title.text = heading;
            if (body != null) body.text = text;
            if (footer != null) footer.text = note ?? "";
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
            Closed?.Invoke();
        }
    }
}
