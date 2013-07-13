using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Stacky
{
  public class NotificationResponse : Response
  {
    [JsonProperty("items")]
    public List<Notification> Notifications { get; set; }
  }
}
