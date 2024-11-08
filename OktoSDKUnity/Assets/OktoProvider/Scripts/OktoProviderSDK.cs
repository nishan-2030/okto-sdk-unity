using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;
using System.Reflection;

namespace OktoProvider
{
    public class OktoProviderSDK : MonoBehaviour
    {
        private static HttpClient httpClient;
        private readonly string apiKey;
        private AuthDetails authDetails;
        private readonly string baseUrl;
        private int JOB_MAX_RETRY = 50;
        private int JOB_RETRY_INTERVAL = 2;
        public OktoProviderSDK(string apiKey, string buildType)
        {
            this.apiKey = apiKey;
            baseUrl = GetBaseUrl(buildType);
            httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(baseUrl);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        }

        private string GetBaseUrl(string buildType)
        {
            if (buildType == "Production")
            {
                return "https://apigw.okto.tech";
            }
            else if (buildType == "Staging")
            {
                return "https://3p-bff.oktostage.com";
            }
            else
            {
                return "https://sandbox-api.okto.tech";
            }
        }

        public void Logout()
        {
            var newAuthDetails = new AuthDetails
            {
                authToken = "",
                refreshToken = "",
                deviceToken = ""
            };

            _ = UpdateAuthDetails(newAuthDetails);
        }

        private void SetAuthorizationHeader(string authToken)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }

        public async Task UpdateAuthDetails(AuthDetails newAuthDetails)
        {
            DataManager.Instance.AuthToken = newAuthDetails.authToken;
            DataManager.Instance.RefreshToken = newAuthDetails.refreshToken;
            DataManager.Instance.DeviceToken = newAuthDetails.deviceToken;
            authDetails = newAuthDetails;
            SetAuthorizationHeader(authDetails.authToken);
            await SaveAuthDetailsToLocalStorage(authDetails);
        }

        private async Task<AuthDetails> LoadAuthDetailsFromLocalStorage()
        {
            string storedAuthDetails = "";
            return JsonConvert.DeserializeObject<AuthDetails>(storedAuthDetails);
        }

        private async Task SaveAuthDetailsToLocalStorage(AuthDetails details)
        {
            string authDetailsJson = JsonConvert.SerializeObject(details);
        }


        public async Task<(AuthDetails result, Exception error)> AuthenticateAsync(string idToken)
        {
            if (httpClient == null)
            {
                return (null, new Exception("SDK is not initialized"));
            }

            try
            {
                var requestBody = new
                {
                    id_token = idToken
                };
                var jsonContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.198 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
                httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");

                var response = await httpClient.PostAsync($"{baseUrl}/api/v2/authenticate", jsonContent);
                Debug.Log(response);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    Debug.Log("Response Content: " + responseContent);

                    var responseData = JsonConvert.DeserializeObject<AuthResponse>(responseContent);
                    Debug.Log(responseData.status);
                    if (responseData?.status == "success" && responseData?.data?.auth_token != null)
                    {
                        var authDetailsNew = new AuthDetails
                        {
                            authToken = responseData.data.auth_token,
                            refreshToken = responseData.data.refresh_auth_token,
                            deviceToken = responseData.data.device_token
                        };
                        UpdateAuthDetails(authDetailsNew);
                        return (authDetailsNew, null);
                    }
                    return (null, new Exception("Server responded with an error: " + responseContent));
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return (null, new Exception("Server responded with an error: " + errorContent));
                }
            }
            catch (Exception ex)
            {
                return (null, ex);
            }
        }

        public async Task<AuthDetails> RefreshToken()
        {
            if (authDetails != null)
            {
                try
                {

                    var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/refresh_token");
                    request.Headers.Add("x-refresh-authorization", $"Bearer {authDetails.refreshToken}");
                    request.Headers.Add("x-device-token", authDetails.deviceToken);

                    var response = await httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();
                    var authResponse = JsonConvert.DeserializeObject<AuthResponse>(content);

                    var newAuthDetails = new AuthDetails
                    {
                        authToken = authResponse.data.auth_token,
                        refreshToken = authResponse.data.refresh_auth_token,
                        deviceToken = authResponse.data.device_token
                    };

                    await UpdateAuthDetails(newAuthDetails);
                    return newAuthDetails;
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to refresh token: " + ex.Message);
                }
            }
            return null;
        }

        public async Task<ApiResponse<T>> MakeGetRequest<T>(string endpoint, string queryUrl = null)
        {
            var url = queryUrl != null ? $"{endpoint}?{queryUrl}" : endpoint;

            try
            {
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.198 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + DataManager.Instance.AuthToken);
                var response = await httpClient.GetAsync($"{baseUrl}/api"+url);
                Debug.Log(response);
                Debug.Log($"{baseUrl}/api" + url);
                Debug.Log(DataManager.Instance.AuthToken);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                Debug.Log(content);
                var apiResponse = JsonConvert.DeserializeObject<ApiResponse<T>>(content);
                Debug.Log(apiResponse.data);
                if (apiResponse.status == "success")
                {
                    return apiResponse;
                }

                throw new Exception("Server responded with an error.");
            }
            catch (Exception ex)
            {
                throw new Exception("Request failed: " + ex.Message);
            }
        }

        public async Task<ApiResponse<T>> MakePostRequest<T>(string endpoint, object data = null)
        {
            try
            {
                var jsonContent = JsonConvert.SerializeObject(data);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.198 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + DataManager.Instance.AuthToken);
                Debug.Log(DataManager.Instance.AuthToken);
                Debug.Log(jsonContent);
                var response = await httpClient.PostAsync($"{baseUrl}/api" + endpoint, content);
               

                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonConvert.DeserializeObject<ApiResponse<T>>(responseContent);

                if (apiResponse.status == "success")
                {
                    return apiResponse;
                }

                throw new Exception("Server responded with an error.");
            }
            catch (Exception ex)
            {
                throw new Exception("Request failed: " + ex.Message);
            }
        }

        public async Task<PortfolioData> GetPortfolio()
        {
            ApiResponse<PortfolioData> response = await MakeGetRequest<PortfolioData>("/v1/portfolio");
            return response.data;
        }
        public async Task<TokensDataNetworks> GetSupportedNetworks()
        {
            ApiResponse<TokensDataNetworks> response = await MakeGetRequest<TokensDataNetworks>("/v1/supported/networks");
            return response.data;
        }
        public async Task<TokensData> GetSupportedTokens()
        {
            ApiResponse<TokensData> response = await MakeGetRequest<TokensData>("/v1/supported/tokens");
            return response.data;
        }

        public async Task<User> GetUserDetails()
        {
            ApiResponse<User> response = await MakeGetRequest<User>("/v1/user_from_token");
            return response.data;
        }

        public async Task<OrderData> OrderHistory(OrderQuery query)
        {
            string queryString = GetQueryString(query);
            ApiResponse<OrderData> response = await MakeGetRequest<OrderData>($"/v1/orders");
            return response.data;
        }


        public async Task<WalletData> GetWallets()
        {
            ApiResponse<WalletData> response = await MakeGetRequest<WalletData>("/v1/wallet");
            return response.data;
        }
        public async Task<NftOrderDetailsData> GetNftOrderDetails(NftOrderDetailsQuery query)
        {
            string queryString = GetQueryString(query);
            ApiResponse<NftOrderDetailsData> response = await MakeGetRequest<NftOrderDetailsData>($"/v1/nft/order_details?{queryString}");
            return response.data;
        }

        public async Task<RawTransactionStatusData> GetRawTransactionStatus(RawTransactionStatusQuery query)
        {
            string queryString = GetQueryString(query);
            ApiResponse<RawTransactionStatusData> response = await MakeGetRequest<RawTransactionStatusData>($"/v1/rawtransaction/status?{queryString}");
            return response.data;
        }
        public async Task<WalletData> CreateWallet()
        {
            ApiResponse<WalletData> response = await MakePostRequest<WalletData>("/v1/wallet");
            return response.data;
        }
        public async Task<TransferTokensData> TransferTokens_(TransferTokens data)
        {
            ApiResponse<TransferTokensData> response = await MakePostRequest<TransferTokensData>("/v1/transfer/tokens/execute", data);
            return response.data;
        }
        public async Task<Order> TransferTokensWithJobStatus(TransferTokens data)
        {
            var transferResponse = await TransferTokens_(data);
            string orderId = transferResponse.orderId;

            return await WaitForJobCompletion(orderId, async (id) =>
            {
                var orderData = await OrderHistory(new OrderQuery { order_id = id });
                return orderData.jobs.FirstOrDefault(job => job.order_id == id && (job.status == "success" || job.status == "failed"));
            });
        }

        public async Task<ExecuteRawTransactionData> executeRawTransaction(ExecuteRawTransaction data)
        {
            ApiResponse<ExecuteRawTransactionData> response = await MakePostRequest<ExecuteRawTransactionData>($"/v1/rawtransaction/execute?network_name={data.network_name}", data);
            return response.data;
        }

        public async Task<RawTransactionStatus> ExecuteRawTransactionWithJobStatus(ExecuteRawTransaction data)
        {
            try
            {
                var jobId = await executeRawTransaction(data);
                Debug.Log($"Execute Raw transaction called with Job ID {jobId}");

                return await WaitForJobCompletion<RawTransactionStatus>(
                    jobId.jobId,
                    async (string orderId) =>
                    {
                        RawTransactionStatusQuery query = new RawTransactionStatusQuery();
                        query.order_id = orderId;
                        var orderData = await GetRawTransactionStatus(query);
                        var order = orderData.jobs.Find(item => item.order_id == orderId);

                        if (order != null &&
                            (order.status == "success" || order.status == "failed"))
                        {
                            return order;
                        }

                        throw new Exception($"Order with ID {orderId} not found or not completed.");
                    }
                );
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.Message);
                throw;
            }
        }

        private async Task<T> WaitForJobCompletion<T>(string orderId, Func<string, Task<T>> findJobCallback)
        {
            for (int retryCount = 0; retryCount < JOB_MAX_RETRY; retryCount++)
            {
                try
                {
                    return await findJobCallback(orderId);
                }
                catch
                {
                    // Ignore exception to allow retry
                }
                await Task.Delay(JOB_RETRY_INTERVAL);
            }
            throw new Exception($"Order ID {orderId} not found or not completed.");
        }
        public string GetQueryString(object query)
        {
            var queryParams = new List<string>();

            // Use reflection to get properties and their values
            foreach (var property in query.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = property.GetValue(query)?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    queryParams.Add($"{property.Name}={value}");
                }
            }

            return string.Join("&", queryParams);
        } 

        public async Task<TransferNftData> transferNft(TransferNft data)
        {
            ApiResponse<TransferNftData> response = await MakePostRequest<TransferNftData>("/v1/nft/transfer", data);
            return response.data;
        }       
    }
    public class Order
    {
        public string order_id { get; set; }
        public string network_name { get; set; }
        public string order_type { get; set; }
        public string status { get; set; }
        public string transaction_hash { get; set; }
    }

    public class ApiResponse<T>
    {
        public T data { get; set; }
        public string status { get; set; }
    }

    [System.Serializable]
    public class AuthDetails
    {
        public string authToken { get; set; }
        public string refreshToken { get; set; }
        public string deviceToken { get; set; }
    }

    public class AuthResponse
    {
        public AuthResponseData data { get; set; }
        public string status { get; set; }
    }

    public class AuthResponseData
    {
        public string auth_token { get; set; }
        public string refresh_auth_token { get; set; }
        public string device_token { get; set; }
    }

    public class PortfolioData
    {
        public Portfolio[] tokens { get; set; }
        public decimal total { get; set; }
    }

    [System.Serializable]
    public class Portfolio
    {
        public string token_name { get; set; }
        public string token_image { get; set; }
        public string token_address { get; set; }
        public string network_name { get; set; }
        public string quantity { get; set; }
        public string amount_in_inr { get; set; }
    }

    [System.Serializable]
    public class TokensDataNetworks
    {
        public List<TokenNetwork> network { get; set; }
    }

    public class TokensData
    {
        public List<Token> tokens { get; set; }
    }

    [System.Serializable]
    public class TokenNetwork
    {
        public string network_name { get; set; }
        public string chain_id { get; set; }
        public string logo { get; set; }
    }

    [System.Serializable]
    public class Token
    {
        public string token_name { get; set; }
        public string token_address { get; set; }
        public string network_name { get; set; }

    }

    public class User
    {
        public string email { get; set; }
        public string user_id { get; set; }
        public string created_at { get; set; }
        public string freezed { get; set; }
        public string freeze_reason { get; set; }
    }

    public class TransferTokensData
    {
        public string orderId { get; set; }
    }

    public class NftOrderDetails
    {
        public string explorer_smart_contract_url { get; set; }
        public string description { get; set; }
        public string type { get; set; }
        public string collection_id { get; set; }
        public string collection_name { get; set; }
        public string nft_token_id { get; set; }
        public string token_uri { get; set; }
        public string id { get; set; }
        public string image { get; set; }
        public string collection_address { get; set; }
        public string collection_image { get; set; }
        public string network_name { get; set; }
        public string network_id { get; set; }
        public string nft_name { get; set; }
    }

    public class TransferNftData
    {
        public string order_id { get; set; }
    }

    public class OrderQuery
    {
        public int offset { get; set; }
        public int limit { get; set; }
        public string order_id { get; set; }
        public string order_state { get; set; }
    }

    public class Wallet
    {
        public string network_name { get; set; }
        public string address { get; set; }
        public bool success { get; set; }
    }

    public class WalletData
    {
        public List<Wallet> wallets { get; set; }
    }

    public class RawTransactionStatusQuery
    {
        public string order_id { get; set; }
    }

    public class TransferTokens
    {
        public string network_name { get; set; }
        public string token_address { get; set; }
        public string quantity { get; set; }
        public string recipient_address { get; set; }
    }

    public class RawTransactionStatus
    {
        public string order_id { get; set; }
        public string network_name { get; set; }
        public string status { get; set; }
        public string transaction_hash { get; set; }
    }

    public class RawTransactionStatusData
    {
        public int total { get; set; }
        public List<RawTransactionStatus> jobs { get; set; }
    }

    public class ExecuteRawTransaction
    {
        public string network_name { get; set; }
        public object transaction { get; set; }
    }

    public class ExecuteRawTransactionData
    {
        public string jobId { get; set; }
    }

    public class OrderData
    {
        public int total { get; set; }
        public List<Order> jobs { get; set; }
    }

    public class NftOrderDetailsData
    {
        public int count { get; set; }
        public List<NftOrderDetails> nfts { get; set; }
    }

    public class NftOrderDetailsQuery
    {
        public int page { get; set; }
        public int size { get; set; }
        public string order_id { get; set; }
    }

    public class TransferNft
    {
        public string operation_type { get; set; }
        public string network_name { get; set; }
        public string collection_address { get; set; }
        public string collection_name { get; set; }
        public string quantity { get; set; }
        public string recipient_address { get; set; }
        public string nft_address { get; set; }
    }

    public class SOLTransaction 
    {
        public List<Instruction> instructions { get; set; }  
        public string signer { get; set; }
    }

    public class Instruction
    {
        public string programId { get; set; }         
        public byte[] data { get; set; }               
        public List<AccountMeta> keys { get; set; }    
    }

    public class AccountMeta
    {
        public string pubkey { get; set; }      
        public bool isSigner { get; set; }     
        public bool isWritable { get; set; }      
    }

    public class EVMTransaction 
    {
        public string from { get; set; }
        public string to { get; set; }
        public string data { get; set; }
        public string value { get; set; }
    }

}

