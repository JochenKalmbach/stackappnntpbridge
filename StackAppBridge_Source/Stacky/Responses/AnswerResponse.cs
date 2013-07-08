using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stacky
{
    public class AnswerResponse : Response
    {
        [JsonProperty("items")]
        public List<Answer> Answers { get; set; }
    }
}
