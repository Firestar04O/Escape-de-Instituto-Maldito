using System.Collections.Generic;
using UnityEngine;

namespace MenuUIBase.UI
{
    [DisallowMultipleComponent]
    public sealed class PanelNavigator : MonoBehaviour
    {
        [SerializeField] private GameObject initialPanel;
        [SerializeField] private List<GameObject> panels = new List<GameObject>();

        private readonly Stack<GameObject> history = new Stack<GameObject>();
        private GameObject currentPanel;

        public GameObject CurrentPanel => currentPanel;

        public void Configure(GameObject firstPanel, IEnumerable<GameObject> managedPanels)
        {
            initialPanel = firstPanel;
            panels.Clear();
            if (managedPanels != null)
                panels.AddRange(managedPanels);
            ResetToInitialPanel();
        }

        private void Awake()
        {
            ResetToInitialPanel();
        }

        public void Open(GameObject panel)
        {
            if (panel == null || panel == currentPanel)
                return;

            if (currentPanel != null)
            {
                history.Push(currentPanel);
                currentPanel.SetActive(false);
            }

            panel.SetActive(true);
            currentPanel = panel;
        }

        public void Back()
        {
            if (history.Count == 0)
            {
                ResetToInitialPanel();
                return;
            }

            if (currentPanel != null)
                currentPanel.SetActive(false);

            currentPanel = history.Pop();
            if (currentPanel != null)
                currentPanel.SetActive(true);
        }

        public void ResetToInitialPanel()
        {
            history.Clear();
            SetAllPanelsInactive();
            currentPanel = initialPanel;

            if (currentPanel != null)
                currentPanel.SetActive(true);
        }

        public void CloseAll()
        {
            history.Clear();
            SetAllPanelsInactive();
            currentPanel = null;
        }

        private void SetAllPanelsInactive()
        {
            foreach (GameObject panel in panels)
            {
                if (panel != null)
                    panel.SetActive(false);
            }

            if (initialPanel != null && !panels.Contains(initialPanel))
                initialPanel.SetActive(false);
        }
    }
}
