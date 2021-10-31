using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class DraconRest
{
    public event EventHandler<RESTRequestResponse> NetworkErrorOccurred;

    string BaseEndPoint;
    string APIQueryParameter;
    string AuthToken;
    string BearerTokenAuthHeaderPrefix;
    string AuthHeader => BearerTokenAuthHeaderPrefix + AuthToken;

    bool useAuthToken;

    public DraconRest (string _baseEndPoint, string _queryParameter, bool _useAuthToken = false, string _authHeaderPrefix = "")
    {
        BaseEndPoint = _baseEndPoint;
        APIQueryParameter = _queryParameter;
        useAuthToken = _useAuthToken;
        BearerTokenAuthHeaderPrefix = _authHeaderPrefix;
    }

    public async void SubmitSampleGETRequest(string path)
    {
        if (!string.IsNullOrEmpty(BaseEndPoint))
        {
            RESTRequestResponse response = await GetAsync(path, null);

            if (response.RequestFailed)
            {

                if (response.RequestWasUnauthorized())
                {
                    // On Unauthorized
                    ClearAuthToken();
                }

                // On Failed
            }
            else
            {
                SampleResponseData sampleData = JsonConvert.DeserializeObject<SampleResponseData>(response.JSONString);

                // On Success (sampleData)
            }
        }
        else
        {
            Debug.LogError("FetchSampleRequest() End point URL not specified");
        }
    }
    public void ClearAuthToken()
    {
        AuthToken = null;
    }

    public bool HaveValidAuthToken()
    {
        if (!useAuthToken)
            return true;
        else
            return !string.IsNullOrEmpty(AuthToken);
    }

    private UriBuilder CreateUriBuilder(string path, string[] additionalQueryParameters)
    {
        UriBuilder uriBuilder = new UriBuilder(BaseEndPoint);
        uriBuilder.Path = path;
        uriBuilder.Query = APIQueryParameter;
        if (additionalQueryParameters != null && additionalQueryParameters.Length > 0)
        {
            uriBuilder.Query += "&" + additionalQueryParameters.Join("&");
        }
        return uriBuilder;
    }

    private void SetRequestResponseFromException(Exception e, string method, RESTRequestResponse requestResponse)
    {
        WebException webException = e as WebException;
        if (webException != null)
        {
            requestResponse.ConnectionStatus = webException.Status;
            if (webException.Status == WebExceptionStatus.ProtocolError)
            {
                HttpWebResponse response = (HttpWebResponse)webException.Response;
                requestResponse.ErrorMessage = string.Format("Errorcode: {0}", response.StatusCode);
                requestResponse.StatusCode = response.StatusCode;

                Debug.LogError($"{method} - {requestResponse.ErrorMessage}");
            }
            else
            {
                requestResponse.ErrorMessage = string.Format("Error: {0}", webException.Status);
                Debug.LogError($"{method} - {requestResponse.ErrorMessage}");
                Debug.LogException(e);
            }
        }
        else
        {
            requestResponse.ConnectionStatus = WebExceptionStatus.UnknownError;
            requestResponse.ErrorMessage = string.Format("Exception: {0}", e.Message);

            Debug.LogException(e);
        }
    }

    private async Task<RESTRequestResponse> PerformRequest(HttpWebRequest request)
    {
        var content = new MemoryStream();

        RESTRequestResponse requestResponse = new RESTRequestResponse();

        DateTimeOffset startTime = DateTimeOffset.Now;
        bool success = false;

        try
        {
            using (WebResponse response = await request.GetResponseAsync())
            {
                using (Stream responseStream = response.GetResponseStream())
                {
                    await responseStream.CopyToAsync(content);

                    byte[] contentBytes = content.ToArray();
                    requestResponse.JSONString = Encoding.UTF8.GetString(contentBytes, 0, contentBytes.Length);

                    HttpWebResponse httpResponse = (HttpWebResponse)response;

                    requestResponse.StatusCode = httpResponse.StatusCode;

                    success = true;
                }
            }
        }
        catch (WebException e)
        {
            SetRequestResponseFromException(e, "PerformRequest()", requestResponse);
            Debug.LogError($"PerformRequest() - exception for request: {request.Address}, {request.Headers}");

            if (e.Status != WebExceptionStatus.ProtocolError)
            {
                NetworkErrorOccurred?.Invoke(this, requestResponse);
            }
        }
        catch (Exception e)
        {
            SetRequestResponseFromException(e, "PerformRequest()", requestResponse);
            NetworkErrorOccurred?.Invoke(this, requestResponse);
        }
        finally
        {
            DateTimeOffset endTime = DateTimeOffset.Now;
            TimeSpan duration = endTime - startTime;
        }
        return requestResponse;
    }

    private async Task<RESTRequestResponse> GetAsync(string path, string[] additionalQueryParameters)
    {
        UriBuilder uriBuilder;
        HttpWebRequest webReq;

        try
        {
            uriBuilder = CreateUriBuilder(path, additionalQueryParameters);

            webReq = (HttpWebRequest)WebRequest.Create(uriBuilder.Uri);
            webReq.Method = WebRequestMethods.Http.Get;

            if (HaveValidAuthToken() && useAuthToken)
            {
                webReq.Headers.Add("Authorization", AuthHeader);
            }
        }
        catch (Exception e)
        {
            RESTRequestResponse requestResponse = new RESTRequestResponse();
            SetRequestResponseFromException(e, "GetAsync()", requestResponse);

            WebException webException = e as WebException;
            if (webException != null)
            {
                if (webException.Status != WebExceptionStatus.ProtocolError)
                {
                    NetworkErrorOccurred?.Invoke(this, requestResponse);
                }
            }
            return requestResponse;
        }

        return await PerformRequest(webReq);
    }

    private async Task<RESTRequestResponse> PostJSONAsync(string path, string jsonString)
    {
        UriBuilder uriBuilder;
        HttpWebRequest webReq;

        try
        {
            uriBuilder = CreateUriBuilder(path, null);

            webReq = (HttpWebRequest)WebRequest.Create(uriBuilder.Uri);
            webReq.Method = WebRequestMethods.Http.Post;
            webReq.ContentType = "application/json";

            // [VIN] - version 2 of the api allows us to add/modify API features, whilst providing ongoing support to builds in the wild!
            webReq.Headers.Add("X-Client-Version", "2");

            if (HaveValidAuthToken() && useAuthToken)
            {
                webReq.Headers.Add("Authorization", AuthHeader);
            }
        }
        catch (Exception e)
        {
            RESTRequestResponse requestResponse = new RESTRequestResponse();
            SetRequestResponseFromException(e, "PostJSONAsync()", requestResponse);

            WebException webException = e as WebException;
            if (webException != null)
            {
                if (webException.Status != WebExceptionStatus.ProtocolError)
                {
                    NetworkErrorOccurred?.Invoke(this, requestResponse);
                }
            }

            return requestResponse;
        }

        if (!string.IsNullOrEmpty(jsonString))
        {
            try
            {
                using (var streamWriter = new StreamWriter(webReq.GetRequestStream()))
                {
                    streamWriter.Write(jsonString);
                }
            }
            catch (WebException e)
            {
                RESTRequestResponse requestResponse = new RESTRequestResponse();
                SetRequestResponseFromException(e, "PostJSONAsync()", requestResponse);
                Debug.LogError($"PostJSONAsync() - No internet connection? {requestResponse.ErrorMessage}");

                if (e.Status != WebExceptionStatus.ProtocolError)
                {
                    NetworkErrorOccurred?.Invoke(this, requestResponse);
                }

                return requestResponse;
            }
            catch (Exception e)
            {
                RESTRequestResponse requestResponse = new RESTRequestResponse();
                SetRequestResponseFromException(e, "PostJSONAsync()", requestResponse);
                return requestResponse;
            }
        }
        else
        {
            webReq.ContentLength = 0;
        }

        return await PerformRequest(webReq);
    }
}

public class RESTRequestResponse
{
    public string JSONString { get; set; }

    public string ErrorMessage { get; set; }

    public HttpStatusCode StatusCode { get; set; }

    public WebExceptionStatus ConnectionStatus { get; set; }

    public bool ShouldRetryFailure { get; set; }

    public bool RequestFailed => ConnectionStatus != WebExceptionStatus.Success || !IsSuccessStatusCode(StatusCode);

    public string DiagnosticInfoJSON => JsonConvert.SerializeObject(DiagnosticInfo);

    public Dictionary<string, string> DiagnosticInfo => new Dictionary<string, string> { { "errorMessage", ErrorMessage }, { "statusCode", StatusCode.ToString() }, { "connectionStatus", ConnectionStatus.ToString() } };

    public bool RequestWasUnauthorized()
    {
        return (ConnectionStatus == WebExceptionStatus.ProtocolError) && (StatusCode == HttpStatusCode.Unauthorized);
    }

    private static bool IsSuccessStatusCode(HttpStatusCode statusCode)
    {
        return ((int)statusCode >= 200) && ((int)statusCode <= 299);
    }
}

public class BaseRESTResponseData
{
    public string ErrorMessage { get; set; }
}

public class SampleResponseData : BaseRESTResponseData
{
    [JsonProperty("sampleKey")]
    public string SampleKey { get; set; }
}

public static class RESTExtensions
{
    public static string Join(this IList<string> items, string joiner)
    {
        StringBuilder b = new StringBuilder();

        if (items == null)
        {
            return "";
        }
        int itemCount = items.Count;

        if (itemCount == 0)
        {
            return "";
        }

        if (itemCount == 1)
        {
            return items[0];
        }

        int i = 0;
        for (; i < itemCount - 1; i++)
        {
            b.Append(items[i]);
            b.Append(joiner);
        }
        b.Append(items[itemCount - 1]);
        return b.ToString();
    }
}

