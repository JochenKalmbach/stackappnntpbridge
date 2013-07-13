using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Stacky
{
  /// <summary>
  /// https://api.stackexchange.com/docs/types/inbox-item
  /// </summary>
  [JsonObject]
  public class InboxItem
  {
    public string ToJson()
    {
      return JsonConvert.SerializeObject(this,
        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }
    public static InboxItem FromJson(string text)
    {
      return JsonConvert.DeserializeObject<InboxItem>(text);
    }

    [JsonProperty("answer_id")]
    public int AnswerId { get; set; }

    [JsonProperty("body")]
    public string Body { get; set; }
  
    [JsonProperty("comment_id")]
    public int CommentId { get; set; }

    [JsonProperty("creation_date"), JsonConverter(typeof(UnixDateTimeConverter))]
    public DateTime CreationDate { get; set; }

    [JsonProperty("is_unread")]
    public bool IsUnread { get; set; }

    [JsonProperty("item_type")]
    public string ItemType { get; set; }

    [JsonProperty("link")]
    public string Link { get; set; }

    [JsonProperty("question_id")]
    public int QuestionId { get; set; }

    [JsonProperty("site")]
    public Site Site { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }
  }
}
