using System;
using UnityEngine;
using UnityEngine.UI;

namespace NostrWalletConnect
{
    public class NWCScannerManager : MonoBehaviour
    {
        [Header("NWC References")]
        [SerializeField] private NostrWalletConnect nwc;
        [SerializeField] private InputField connectionStringInput;
 
        [SerializeField] private Button scanQRButton;

        [Header("UI Feedback")]
        [SerializeField] private Text statusText;

        private void Start()
        {
            SetupQRScanner();
            SetupUI();
        }

        private void SetupUI()
        {
            if (scanQRButton != null)
            {
                scanQRButton.onClick.AddListener(StartQRScan);
            }
        }

        private void SetupQRScanner()
        {
            
        }

        public void StartQRScan()
        { 
        }

        private void OnQRCodeDetected(string qrText)
        {
            try
            {
                UpdateStatus("Validating NWC connection string...");

                var connectionString = NWCConnectionString.Parse(qrText);

                if (connectionStringInput != null)
                {
                    connectionStringInput.text = qrText;
                }

                UpdateStatus("NWC connection string loaded successfully!");

                
                if (nwc != null && connectionString != null)
                {
                    AutoConnectPrompt(qrText);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Invalid NWC connection string: {ex.Message}", true);
                Debug.LogError($"QR code parsing error: {ex.Message}");
            }
        }

        private void OnScanError(string error)
        {
            UpdateStatus($"Scan error: {error}", true);
        }

        private void AutoConnectPrompt(string connectionString)
        {
            UpdateStatus("Connection string loaded. Tap Connect to proceed.");

#if UNITY_EDITOR
            Debug.Log($"Auto-connect would use: {connectionString}");
#endif
        }

        private void UpdateStatus(string message, bool isError = false)
        {
            if (statusText != null)
            {
                statusText.text = $"{DateTime.Now:HH:mm:ss} - {message}";
                statusText.color = isError ? Color.red : Color.green;
            }

            Debug.Log($"NWC Scanner: {message}");
        }

        public void TestQRScan()
        {
            var testConnectionString = "nostr+walletconnect://b889ff5b1513b641e2a139f661a661364979c5beee91842f8f0e8dda6766a3b3?relay=wss://relay.primal.net&secret=71a8c14c1407c113601079c4302dab36460f0ccd0ad506f884658492666e639d";

            UpdateStatus("Testing QR detection...");
            OnQRCodeDetected(testConnectionString);
        }

        private void OnDestroy()
        {
           
        }
    }
}