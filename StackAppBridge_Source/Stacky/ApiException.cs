using System;

namespace Stacky
{
    public class ApiException : Exception
    {
        public ResponseError Error { get; set; }
        public Uri Url {get;set;}

        public string Body { get; set; }

        public ApiException() { }

        public ApiException(ResponseError error)
            : this(error.Message, error, null, null, null)
        {
        }

        public ApiException(ResponseError error, string body)
          : this(error.Message, error, null, null, body)
        {
        }
        
        
        public ApiException(ResponseError error, Exception innerException, Uri url) 
          : this(error.Message, error, innerException, url, null)
        {
        }

        public ApiException(Exception innerException, Uri url)
            : base("Error with url: " + url.ToString(), innerException)
        {
            Url = url;
        }

        public ApiException(Exception innerException)
          : base(innerException.Message, innerException)
        {
        }

        public ApiException(Exception innerException, string body)
          : this(innerException.Message, null, innerException, null, body)
        {
        }

        public ApiException(string message, ResponseError error, Exception innerException, Uri url, string body)
            : base(message, innerException)
        {
            Error = error;
            Url = url;
          Body = body;
        }
    }
}