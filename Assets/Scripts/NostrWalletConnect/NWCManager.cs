using System;
using System.Threading.Tasks;
using UnityEngine;

namespace NostrWalletConnect
{
    public class NWCManager : MonoBehaviour
    {
        [Header("Connection Options")]
        [SerializeField] private bool useServerMode = false;
        [SerializeField] private string connectionString = "";

        [Header("Direct Client Components")]
        [SerializeField] private NostrWalletConnect directClient;

        [Header("Server Client Components")]
        [SerializeField] private ServerNWCClient serverClient;

        [Header("Crypto Testing")]
        [SerializeField] private NostrCryptoTest cryptoTest;

        public bool IsConnected => useServerMode ?
            (serverClient != null && serverClient.IsConnected) :
            (directClient != null && directClient.IsConnected);

        public string WalletPubkey => useServerMode ?
            serverClient?.WalletPubkey :
            directClient?.WalletPubkey;

        private void Start()
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = "nostr+walletconnect://b05f0a480b8b290dfed00f67558971f25aae8aa36ecf587ac9b726f71cd80ac9?relay=wss://relay.getalby.com/v1&secret=1b295d78da729c4e37bd909568a7c27307a8381a3d59d7441cc7d6e6f7702280&lud16=chriszbd@getalby.com";
            }

            SetupClients();
        }

        private void SetupClients()
        {
            if (useServerMode)
            {
                SetupServerMode();
            }
            else
            {
                SetupDirectMode();
            }
        }

        private void SetupServerMode()
        {
            Debug.Log("Setting up Server Mode NWC Client");

            if (serverClient == null)
            {
                serverClient = gameObject.GetComponent<ServerNWCClient>();
                if (serverClient == null)
                {
                    serverClient = gameObject.AddComponent<ServerNWCClient>();
                }
            }

            serverClient.SetServerUrl("http://localhost:3000");
            serverClient.SetConnectionString(connectionString);

            serverClient.OnConnected += () => Debug.Log("Server NWC Client Connected!");
            serverClient.OnDisconnected += () => Debug.Log("Server NWC Client Disconnected");
            serverClient.OnError += (error) => Debug.LogError($"Server NWC Client Error: {error}");

            if (directClient != null)
            {
                directClient.enabled = false;
            }
        }

        private void SetupDirectMode()
        {
            Debug.Log("Setting up Direct Mode NWC Client (with NBitcoin crypto)");

            if (directClient == null)
            {
                directClient = gameObject.GetComponent<NostrWalletConnect>();
                if (directClient == null)
                {
                    directClient = gameObject.AddComponent<NostrWalletConnect>();
                }
            }

            directClient.SetConnectionString(connectionString);
            directClient.SetDebugMode(true);

            directClient.OnConnected += () => Debug.Log("Direct NWC Client Connected!");
            directClient.OnDisconnected += () => Debug.Log("Direct NWC Client Disconnected");
            directClient.OnError += (error) => Debug.LogError($"Direct NWC Client Error: {error}");

            if (serverClient != null)
            {
                serverClient.enabled = false;
            }
        }

        [ContextMenu("Switch to Server Mode")]
        public void SwitchToServerMode()
        {
            useServerMode = true;
            SetupClients();
        }

        [ContextMenu("Switch to Direct Mode")]
        public void SwitchToDirectMode()
        {
            useServerMode = false;
            SetupClients();
        }

        [ContextMenu("Connect")]
        public async void Connect()
        {
            try
            {
                if (useServerMode)
                {
                    bool connected = await serverClient.ConnectAsync(connectionString);
                    Debug.Log($"Server connection result: {connected}");
                }
                else
                {
                    bool connected = await directClient.ConnectAsync(connectionString);
                    Debug.Log($"Direct connection result: {connected}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Connection error: {ex.Message}");
            }
        }

        [ContextMenu("Get Balance")]
        public async void GetBalance()
        {
            try
            {
                if (!IsConnected)
                {
                    Debug.LogWarning("Not connected to wallet");
                    return;
                }

                if (useServerMode)
                {
                    long balance = await serverClient.GetBalanceAsync();
                    Debug.Log($"Balance (Server): {balance} millisats");
                }
                else
                {
                    var response = await directClient.GetBalanceAsync();
                    if (response.Error != null)
                    {
                        Debug.LogError($"Error: {response.Error.Code} - {response.Error.Message}");
                    }
                    else
                    {
                        Debug.Log($"Balance (Direct): {response.Result}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Get balance error: {ex.Message}");
            }
        }

        [ContextMenu("Get Info")]
        public async void GetInfo()
        {
            try
            {
                if (!IsConnected)
                {
                    Debug.LogWarning("Not connected to wallet");
                    return;
                }

                if (useServerMode)
                {
                    var info = await serverClient.GetWalletInfoAsync();
                    Debug.Log($"Wallet Info (Server): Alias={info.alias}, Network={info.network}, Pubkey={info.pubkey}");
                }
                else
                {
                    var response = await directClient.GetInfoAsync();
                    if (response.Error != null)
                    {
                        Debug.LogError($"Error: {response.Error.Code} - {response.Error.Message}");
                    }
                    else
                    {
                        Debug.Log($"Wallet Info (Direct): {Newtonsoft.Json.JsonConvert.SerializeObject(response.Result)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Get info error: {ex.Message}");
            }
        }

        [ContextMenu("Create Test Invoice")]
        public async void CreateTestInvoice()
        {
            try
            {
                if (!IsConnected)
                {
                    Debug.LogWarning("Not connected to wallet");
                    return;
                }

                if (useServerMode)
                {
                    var invoice = await serverClient.CreateInvoiceAsync(1000, "Unity Test Invoice", 3600);
                    Debug.Log($"Created Invoice (Server): {invoice.invoice}");
                }
                else
                {
                    var response = await directClient.MakeInvoiceAsync(1000, "Unity Test Invoice", 3600);
                    if (response.Error != null)
                    {
                        Debug.LogError($"Error: {response.Error.Code} - {response.Error.Message}");
                    }
                    else
                    {
                        Debug.Log($"Created Invoice (Direct): {Newtonsoft.Json.JsonConvert.SerializeObject(response.Result)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Create invoice error: {ex.Message}");
            }
        }

        [ContextMenu("Test Crypto Functions")]
        public void TestCrypto()
        {
            if (cryptoTest == null)
            {
                cryptoTest = gameObject.GetComponent<NostrCryptoTest>();
                if (cryptoTest == null)
                {
                    cryptoTest = gameObject.AddComponent<NostrCryptoTest>();
                }
            }

            cryptoTest.TestCryptoFunctions();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 400, 500));

            GUILayout.Label($"NWC Manager - Mode: {(useServerMode ? "Server" : "Direct")}");
            GUILayout.Label($"Connected: {IsConnected}");
            GUILayout.Label($"Wallet: {WalletPubkey}");

            GUILayout.Space(10);

            if (GUILayout.Button("Switch Mode"))
            {
                useServerMode = !useServerMode;
                SetupClients();
            }

            if (GUILayout.Button("Connect"))
            {
                Connect();
            }

            GUILayout.Space(5);

            if (GUILayout.Button("Get Balance"))
            {
                GetBalance();
            }

            if (GUILayout.Button("Get Info"))
            {
                GetInfo();
            }

            if (GUILayout.Button("Create Test Invoice"))
            {
                CreateTestInvoice();
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Test Crypto"))
            {
                TestCrypto();
            }

            GUILayout.EndArea();
        }
    }
}