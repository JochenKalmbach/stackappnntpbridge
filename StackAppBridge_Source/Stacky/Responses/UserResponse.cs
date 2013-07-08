using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stacky
{
    public class UserResponse : Response
    {
        [JsonProperty("items")]
        public List<User> Users { get; set; }
    }
}
