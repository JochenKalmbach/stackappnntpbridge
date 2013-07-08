using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stacky
{
    public abstract class Response
    {
        //[JsonProperty("total")]
        //public int Total { get; set; }

        [JsonProperty("page")]
        public int CurrentPage { get; set; }

        [JsonProperty("pagesize")]
        public int PageSize { get; set; }

        [JsonProperty("has_more")]
        public bool HasMore { get; set; }

        [JsonProperty("backoff")]
        public int BackOff { get; set; }

        [JsonProperty("quota_max")]
        public int QuotaMax { get; set; }

        [JsonProperty("quota_remaining")]
        public int QuotaRemaining { get; set; }

        [JsonProperty("error_id")]
        public int ErrorId { get; set; }

        [JsonProperty("error_name")]
        public string ErrorName { get; set; }

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

      //public ResponseError Error { get; set; }
    }
}