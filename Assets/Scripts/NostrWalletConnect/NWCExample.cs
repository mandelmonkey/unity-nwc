using System;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NostrWalletConnect
{
    public class NWCExample : MonoBehaviour
    {
        [Header("UI References")] 
        [SerializeField] private InputField connectionStringInput;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton; 
        [SerializeField] private Button getInfoButton;
        [SerializeField] private Button requestInfoEventButton;
        [SerializeField] private Button getBalanceButton;
        [SerializeField] private InputField invoiceAmountInput;
        [SerializeField] private InputField invoiceDescriptionInput;
        [SerializeField] private Button makeInvoiceButton;
        [SerializeField] private InputField payInvoiceInput;
        [SerializeField] private Button payInvoiceButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text responseText;
 
        [SerializeField] private ZBDQRScanner qrScanner;
        private string connectionString;
        private void Start()
        {
            SetupUI();
            SetupNWCEvents(); 
            LoadSavedConnectionString();

            qrScanner.OnQRCodeDetected += OnQRCodeDetected;
        }

        private void OnDestroy()
        {
            qrScanner.OnQRCodeDetected -= OnQRCodeDetected;
        }

        private void OnQRCodeDetected(String nwcConnectionString)
        {
            connectionString = nwcConnectionString;
            if (connectionStringInput != null)
            {
                connectionStringInput.text = nwcConnectionString;
            }

            ConnectToWallet();
        }
 
        private void LoadSavedConnectionString()
        { 
                // Try to load saved connection string
                connectionString = ZBDQRScanner.GetSavedConnectionString();

                if (connectionStringInput != null)
                {
                    connectionStringInput.text = connectionString;
                }
  
        }

        private void SetupUI()
        {
            if (connectButton != null)
                connectButton.onClick.AddListener(() => _ = ConnectToWallet());

            if (disconnectButton != null)
                disconnectButton.onClick.AddListener(() => _ = DisconnectFromWallet());
 

            if (getInfoButton != null)
                getInfoButton.onClick.AddListener(() => _ = GetWalletInfo());

            if (requestInfoEventButton != null)
                requestInfoEventButton.onClick.AddListener(() => _ = RequestWalletInfoEvent());

            if (getBalanceButton != null)
                getBalanceButton.onClick.AddListener(() => _ = GetWalletBalance());

            if (makeInvoiceButton != null)
                makeInvoiceButton.onClick.AddListener(() => _ = MakeInvoice());

            if (payInvoiceButton != null)
                payInvoiceButton.onClick.AddListener(() => _ = PayInvoice());

            UpdateButtonStates(false);
        }
   
        private void SetupNWCEvents()
        {
            if (NostrWalletConnect._instance != null)
            {
                NostrWalletConnect._instance.OnConnected += () =>
                {
                    UpdateStatus("Connected to wallet", Color.green);
                    UpdateButtonStates(true);
                };

                NostrWalletConnect._instance.OnDisconnected += () =>
                {
                    UpdateStatus("Disconnected from wallet", Color.red);
                    UpdateButtonStates(false);
                };

                NostrWalletConnect._instance.OnError += (error) =>
                {
                    UpdateStatus($"Error: {error}", Color.red);
                };

                // Use the new event that includes cache information
                NostrWalletConnect._instance.OnResponseWithCache += (response, cacheInfo) =>
                {
                    if (response.Error != null)
                    {
                        DisplayResponse($"[{cacheInfo}] Error: {response.Error.Code} - {response.Error.Message}");
                    }
                    else
                    {
                        DisplayResponse($"[{cacheInfo}] Success: {JsonConvert.SerializeObject(response.Result, Formatting.Indented)}");
                    }
                };

                // Keep the old event for backwards compatibility (but OnResponseWithCache takes precedence)
                NostrWalletConnect._instance.OnResponse += (response) =>
                {
                    // Only log without cache info if needed for debugging
                    Debug.Log($"Response received (legacy event): {response.ResultType}");
                };
            }
        }

        private async Task ConnectToWallet()
        {
            
            if (string.IsNullOrEmpty(this.connectionString) && connectionStringInput != null)
            {
                if (!string.IsNullOrEmpty(connectionStringInput.text))
                {
                    connectionString = connectionStringInput.text;
                }
            }
            
            if (string.IsNullOrEmpty(this.connectionString))
            {
                UpdateStatus("Connection String not set", Color.red);
                return;
            }

          

            connectionString = connectionString.Trim();
             
            try
            {
                UpdateStatus("Connecting...", Color.yellow);
                var success = await NostrWalletConnect._instance.ConnectAsync(connectionString);

                if (!success)
                {
                    UpdateStatus("Failed to connect", Color.red);
                }
                else
                {
                    PlayerPrefs.SetString("NWC_ConnectionString", connectionString);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Connection error: {ex.Message}", Color.red);
            }
        }

        private async Task DisconnectFromWallet()
        {
            if (NostrWalletConnect._instance == null) return;

            try
            {
                await NostrWalletConnect._instance.DisconnectAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Disconnect error: {ex.Message}", Color.red);
            }
        }

        private async Task GetWalletInfo()
        {
            if (!IsConnected()) return;

            try
            {
                UpdateStatus("Getting wallet info...", Color.yellow);
                await NostrWalletConnect._instance.GetInfoAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Get info error: {ex.Message}", Color.red);
            }
        }

        private async Task RequestWalletInfoEvent()
        {
            if (!IsConnected()) return;

            try
            {
                UpdateStatus("Requesting wallet info event (kind 13194)...", Color.yellow);
                await NostrWalletConnect._instance.RequestWalletInfoEvent();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Request info event error: {ex.Message}", Color.red);
            }
        }

        private async Task GetWalletBalance()
        {
            if (!IsConnected()) return;

            try
            {
                UpdateStatus("Getting wallet balance...", Color.yellow);
                var response = await NostrWalletConnect._instance.GetBalanceAsync();

                // Check for NWC errors
                if (response.HasError)
                {
                    UpdateStatus($"Get balance error: {response.ErrorMessage}", Color.red);
                    return;
                }

                // Success! Display balance
                if (response.Result.TryGetValue("balance", out var balanceObj))
                {
                    UpdateStatus($"Balance: {balanceObj} sats", Color.green);
                }
                else
                {
                    UpdateStatus("Balance retrieved successfully", Color.green);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Get balance error: {ex.Message}", Color.red);
            }
        }

        private async Task MakeInvoice()
        {
            if (!IsConnected()) return;

            try
            {
                var amountText = invoiceAmountInput?.text ?? "1000";
                var description = invoiceDescriptionInput?.text ?? "Test invoice from Unity";

                if (!long.TryParse(amountText, out long amount))
                {
                    UpdateStatus("Invalid amount", Color.red);
                    return;
                }

                UpdateStatus("Creating invoice...", Color.yellow);
                var response = await NostrWalletConnect._instance.MakeInvoiceAsync(amount, description);

                // Check for NWC errors
                if (response.HasError)
                {
                    UpdateStatus($"Make invoice error: {response.ErrorMessage}", Color.red);
                    return;
                }

                // Success! Display invoice
                if (response.Result.TryGetValue("invoice", out var invoiceObj))
                {
                    UpdateStatus($"Invoice created: {invoiceObj}", Color.green);
                }
                else
                {
                    UpdateStatus("Invoice created successfully", Color.green);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Make invoice error: {ex.Message}", Color.red);
            }
        }

        private async Task PayInvoice()
        {
            if (!IsConnected()) return;

            try
            {
                var invoice = payInvoiceInput?.text?.Trim();
                if (string.IsNullOrEmpty(invoice))
                {
                    UpdateStatus("Please enter an invoice", Color.red);
                    return;
                }

                UpdateStatus("Paying invoice...", Color.yellow);
                var response = await NostrWalletConnect._instance.PayInvoiceAsync(invoice);

                // Check for NWC errors
                if (response.HasError)
                {
                    UpdateStatus($"Pay invoice error: {response.ErrorMessage}", Color.red);
                    return;
                }

                // Verify preimage cryptographically
                bool isVerified = response.VerifyPaymentPreimage(invoice);
                var preimage = response.GetPreimage();

                if (isVerified)
                {
                    UpdateStatus($"✅ Payment verified! Preimage: {preimage?.Substring(0, 16)}...", Color.green);
                }
                else
                {
                    // Payment succeeded but preimage verification failed - this is suspicious
                    UpdateStatus($"⚠️ Payment completed but preimage verification FAILED", Color.yellow);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Pay invoice error: {ex.Message}", Color.red);
            }
        }

        private bool IsConnected()
        {
            if (NostrWalletConnect._instance == null || !NostrWalletConnect._instance.IsConnected)
            {
                UpdateStatus("Not connected to wallet", Color.red);
                return false;
            }
            return true;
        }

        private void UpdateStatus(string message, Color color)
        {
            if (statusText != null)
            {
                statusText.text = $"{DateTime.Now:HH:mm:ss} - {message}";
                statusText.color = color;
            }
            Debug.Log($"NWC Status: {message}");
        }

        private void DisplayResponse(string response)
        {
            if (responseText != null)
            {
                responseText.text = response;
            } 
            Debug.Log($"NWC Response: {response}");
        }

        private void UpdateButtonStates(bool connected)
        {
            if (connectButton != null)
                connectButton.interactable = !connected;

            if (disconnectButton != null)
                disconnectButton.interactable = connected;

            if (getInfoButton != null)
                getInfoButton.interactable = connected;

            if (requestInfoEventButton != null)
                requestInfoEventButton.interactable = connected;

            if (getBalanceButton != null)
                getBalanceButton.interactable = connected;

            if (makeInvoiceButton != null)
                makeInvoiceButton.interactable = connected;

            if (payInvoiceButton != null)
                payInvoiceButton.interactable = connected;
        }

        [ContextMenu("Test Connection String Parser")]
        private void TestConnectionStringParser()
        {
            var testConnectionString = "nostr+walletconnect://b889ff5b1513b641e2a139f661a661364979c5beee91842f8f0e8dda6766a3b3?relay=wss://relay.primal.net&secret=71a8c14c1407c113601079c4302dab36460f0ccd0ad506f884658492666e639d";

            try
            {
                var connection = NWCConnectionString.Parse(testConnectionString);
                Debug.Log($"Wallet Pubkey: {connection.WalletPubkey}");
                Debug.Log($"Relay URL: {connection.RelayUrl}");
                Debug.Log($"Secret: {connection.Secret}");
                Debug.Log($"LnURL-P: {connection.LnUrlP}");
                UpdateStatus("Connection string parsing test passed", Color.green);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Connection string parsing failed: {ex.Message}");
                UpdateStatus($"Connection string parsing test failed: {ex.Message}", Color.red);
            }
        }

        [ContextMenu("Test Crypto Functions")]
        private void TestCryptoFunctions()
        {
            try
            {
                var privateKey = NostrCrypto.GeneratePrivateKey();
                var publicKey = NostrCrypto.GetPublicKey(privateKey);

                Debug.Log($"Generated Private Key: {privateKey}");
                Debug.Log($"Generated Public Key: {publicKey}");

                var testMessage = "Hello, Nostr Wallet Connect!";
                var recipientPrivateKey = NostrCrypto.GeneratePrivateKey();
                var recipientPublicKey = NostrCrypto.GetPublicKey(recipientPrivateKey);

                var encrypted = NostrCrypto.EncryptNIP04(testMessage, recipientPublicKey, privateKey);
                var decrypted = NostrCrypto.DecryptNIP04(encrypted, publicKey, recipientPrivateKey);

                Debug.Log($"Original: {testMessage}");
                Debug.Log($"Encrypted: {encrypted}");
                Debug.Log($"Decrypted: {decrypted}");

                if (testMessage == decrypted)
                {
                    UpdateStatus("Crypto functions test passed", Color.green);
                }
                else
                {
                    UpdateStatus("Crypto functions test failed: decryption mismatch", Color.red);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Crypto test failed: {ex.Message}");
                UpdateStatus($"Crypto functions test failed: {ex.Message}", Color.red);
            }
        }
    }
}