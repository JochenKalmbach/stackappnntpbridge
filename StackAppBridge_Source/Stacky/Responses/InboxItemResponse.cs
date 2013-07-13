using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Stacky
{
  public class InboxItemResponse : Response
  {
    [JsonProperty("items")]
    public List<InboxItem> InboxItems { get; set; }
  }
}
