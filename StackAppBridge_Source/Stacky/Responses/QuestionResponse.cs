using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stacky
{
    public class QuestionResponse : Response
    {
        [JsonProperty("items")]
        public List<Question> Questions { get; set; }
    }
}