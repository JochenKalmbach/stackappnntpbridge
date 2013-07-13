using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Stacky
{
  /// <summary>
  /// https://api.stackexchange.com/docs/types/notification
  /// </summary>
  [JsonObject]
  public class Notification
  {
    public string ToJson()
    {
      return JsonConvert.SerializeObject(this,
        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }
    public static Notification FromJson(string text)
    {
      return JsonConvert.DeserializeObject<Notification>(text);
    }

    [JsonProperty("body")]
    public string Body { get; set; }

    [JsonProperty("creation_date"), JsonConverter(typeof(UnixDateTimeConverter))]
    public DateTime CreationDate { get; set; }

    [JsonProperty("is_unread")]
    public bool IsUnread { get; set; }

    [JsonProperty("notification_type")]
    public string NotificationType { get; set; }

    [JsonProperty("post_id")]
    public int PostId { get; set; }

    [JsonProperty("site")]
    public Site Site { get; set; }
  }
}
