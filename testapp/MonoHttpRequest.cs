#region
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using Firebase.Platform;
#endregion

namespace Firebase.Mono {
  /// <summary>
  /// Similar to the version used in UnityHttpRequest, but does not
  /// need to invoke back to the unity thread or do similar garbage.
  /// </summary>
  class MonoHttpRequest : FirebaseHttpRequest {
    private HttpWebRequest _httpWebRequest;
    private HttpWebResponse _httpWebResponse;

    static MonoHttpRequest() {
      ServicePointManager.DefaultConnectionLimit = 2000;
    }

    internal MonoHttpRequest(Uri url) : base(url) {
    }

    public override Stream OutputStream {
      get { return Request.GetRequestStream(); }
    }

    public override int ResponseCode {
      get {
        int result = (int) Response.StatusCode;
        LogUtil.LogMessage(LogLevel.Debug, _url, "result:" + result);
        return result;
      }
    }

    public override NameValueCollection ResponseHeaderFields {
      get {
        NameValueCollection result = Response.Headers;

        if (result != null) {
          foreach (var entry in result.AllKeys) {
            LogUtil.LogMessage(LogLevel.Debug, _url, entry + ":" + result[entry]);
          }
        }
        return Response.Headers;
      }
    }

    public override long ResponseContentLength {
      get { return Response.ContentLength; }
    }

    public override Stream InputStream {
      get { return Response.GetResponseStream(); }
    }

    public override Stream ErrorStream {
      get { return Response.GetResponseStream(); }
    }

    private HttpWebRequest Request {
      get {
        if (_httpWebRequest == null) {
          _httpWebRequest = WebRequest.Create(_url) as HttpWebRequest;
          Debug.Assert(_httpWebRequest != null, "httpWebRequest != null");
          _httpWebRequest.Method = _action;
        }
        return _httpWebRequest;
      }
    }

    private HttpWebResponse Response {
      get {
        if ((_httpWebResponse == null)
            && (Request != null)) {
          try {
            _httpWebResponse = (HttpWebResponse) Request.GetResponse();
          } catch (WebException we) {
            _httpWebResponse = we.Response as HttpWebResponse;
          }
        }
        return _httpWebResponse;
      }
    }

    public override void SetRequestProperty(string key, string value) {
      LogUtil.LogMessage(LogLevel.Debug, _url, key + ":" + value);
      if ("Content-Length".Equals(key)) {
        Request.ContentLength = long.Parse(value);
      } else if ("Content-Type".Equals(key)) {
        Request.ContentType = value;
      } else {
        Request.Headers[key] = value;
      }
    }
  }
}
