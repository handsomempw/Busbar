using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using Newtonsoft.Json.Serialization;

namespace GetSNByMP
{
    /// <summary>
    /// 请求参数
    /// </summary>
    public class Variables : Dictionary<string, string>
    {
    }

    internal struct GraphQlRequest
    {
        public string Query;
        public Variables Variables;

        public GraphQlRequest(string query, Variables variables)
        {
            Query = query;
            Variables = variables;
        }
    }

    internal struct GraphQlError
    {
        public string Message;
    }

    internal class GraphQlResult<T>
    {
        public T Data;
        public GraphQlError[] Errors;
        public bool HasError => Errors != null && Errors.Length > 0;
    }

    /// <summary>
    /// GraphQL 客户端
    /// </summary>
    public class GraphQlClient : IDisposable
    {
        private readonly HttpClient _httpClient;

        private readonly JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };


        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="apiUrl">API地址</param>
        public GraphQlClient(string apiUrl)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
                DefaultRequestHeaders =
                {
                    UserAgent = { new ProductInfoHeaderValue("MES_AUTOLINE_CLIENT", "1.0") }
                },
                BaseAddress = new Uri(apiUrl)
            };
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="query">查询指令</param>
        /// <param name="variables">变量</param>
        /// <typeparam name="T">查询结果类型</typeparam>
        /// <returns>查询结果</returns>
        /// <exception cref="Exception"></exception>
        public T Request<T>(string query, Variables variables = null)
        {
            var request = new GraphQlRequest(query, variables);
            var json = JsonConvert.SerializeObject(request, _jsonSerializerSettings);
            var message = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            var response = _httpClient.SendAsync(message).Result;
            var body = response.Content.ReadAsStringAsync().Result;

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(string.IsNullOrEmpty(body) ? response.ReasonPhrase : body);
            }

            var result = JsonConvert.DeserializeObject<GraphQlResult<T>>(body, _jsonSerializerSettings);

            if (result.HasError)
            {
                throw new Exception(string.Join(";", result.Errors.Select(it => it.Message)));
            }

            return result.Data;
        }
    }
}