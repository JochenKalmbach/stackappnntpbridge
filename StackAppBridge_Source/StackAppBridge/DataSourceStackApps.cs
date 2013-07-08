﻿using System;
using System.Collections.Generic;
using System.Data.Objects;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using StackAppBridge.ArticleConverter;
using StackAppBridge;
using System.Text.RegularExpressions;
using System.Text;
using StackAppBridge.NNTPServer;
using Stacky;

namespace StackAppBridge
{
  internal class DataSourceStackApps : DataProvider
  {
    private NewsgroupConfig _NewsgroupConfig;

    public DataSourceStackApps()
    {
      _management = new MsgNumberManagement(UserSettings.Default.BasePath);

      ClearCache();
    }

    public Encoding HeaderEncoding = Encoding.UTF8;

    private readonly MsgNumberManagement _management;

    #region DataProvider-Implenentation

    public IList<Newsgroup> PrefetchNewsgroupList(Action<Newsgroup> stateCallback)
    {
      LoadNewsgroupsToStream(stateCallback);
      return GroupList.Values.ToList();
    }

    internal const string NewsgroupConfigFileName = "NewsgroupConfig.xml";
    public void ClearCache()
    {
      lock (GroupList)
      {
        GroupList.Clear();
        ResetNewsgroupCacheValid();
        // Re-Read the config...
        _NewsgroupConfig = NewsgroupConfig.Load(Path.Combine(UserSettings.Default.BasePath, NewsgroupConfigFileName));
        UserSettings.Default.Newsgroups = _NewsgroupConfig.Newsgroups;
      }
      LoadNewsgroupsToStream(null);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns>Returns <c>true</c> if now exception was thrown while processing the request</returns>
    /// <remarks>
    /// It might happen that this function is called twice!
    /// For example if you are currently reading the newsgrouplist and then a client is trying to read articles from a subscribed newsgroup...
    /// </remarks>
    protected override bool LoadNewsgroupsToStream(Action<Newsgroup> groupAction)
    {
      bool res = true;
      lock (this)
      {
        if (IsNewsgroupCacheValid())
        {
          // copy the list to a local list, so we do not need the lock for the callback
          List<Newsgroup> localGroups;
          lock (GroupList)
          {
            localGroups = new List<Newsgroup>(GroupList.Values);
          }
          if (groupAction != null)
          {
            foreach (var g in localGroups)
              groupAction(g);
          }
          return true;
        }

        // Use the newsgroup from the config
        foreach (NewsgroupConfigEntry entry in _NewsgroupConfig.Newsgroups)
        {

          var g = new ForumNewsgroup(entry);

          // Update the Msg# from the local mapping database: because this group might already been available in the _groupList...
          lock (GroupList)
          {
            ForumNewsgroup existingGroup = null;
            if (GroupList.ContainsKey(g.GroupName))
            {
              existingGroup = GroupList[g.GroupName] as ForumNewsgroup;
            }
            if (existingGroup != null)
            {
              g = existingGroup; // use the existing group in order to prevent problems with the database
            }
            else
            {
              GroupList.Add(g.GroupName, g);
            }
          }
          _management.GetMaxMessageNumber(g);

          if (groupAction != null)
            groupAction(g);
        }

        if (res)
          SetNewsgroupCacheValid();
      } // lock

      return res;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// It might happen that this function is called twice!
    /// For example if you are currently reading the newsgrouplist and then a client is trying to read articles from a subscribed newsgroup...
    /// </remarks>
    public override bool GetNewsgroupListFromDate(string clientUsername, DateTime fromDate,
                                                  Action<Newsgroup> groupAction)
    {
      //// For now, we just return the whole list; I have not stored the group-data in a database...
      //return GetNewsgroupListToStream(clientUsername, groupAction);
      // Just return! We do not support this currently...
      return true;
    }

    // 
    /// <summary>
    /// 
    /// </summary>
    /// <param name="clientUsername"></param>
    /// <param name="groupName"></param>
    /// <param name="updateFirstLastNumber">If this is <c>true</c>, then always get the newgroup info from the sever; 
    /// so we have always the corrent NNTPMaxNumber!</param>
    /// <returns></returns>
    public override Newsgroup GetNewsgroup(string clientUsername, string groupName, bool updateFirstLastNumber, out bool exceptionOccured)
    {
      exceptionOccured = false;

      // First try to find the group (ServiceProvider) in the cache...
      ForumNewsgroup cachedGroup = null;
      lock (GroupList)
      {
        if (GroupList.ContainsKey(groupName))
        {
          cachedGroup = GroupList[groupName] as ForumNewsgroup;
        }
      }

      // If we just need the group without actual data, then return the cached group
      if ((updateFirstLastNumber == false) && (cachedGroup != null))
        return cachedGroup;

      if (cachedGroup == null)
      {
        // Group not found...
        Traces.Main_TraceEvent(TraceEventType.Verbose, 1,
                               "GetNewsgroup failed (invalid groupname; cachedGroup==null): {0}", groupName);
        return null;
      }

      if (updateFirstLastNumber)
      {
        var articles = _management.UpdateGroupFromWebService(cachedGroup, OnProgressData, ConvertNewArticleFromWebService);
      }

      return cachedGroup;
    }

    public override Newsgroup GetNewsgroupFromCacheOrServer(string groupName)
    {
      if (string.IsNullOrEmpty(groupName))
        return null;
      groupName = groupName.Trim();
      ForumNewsgroup res = null;
      lock (GroupList)
      {
        if (GroupList.ContainsKey(groupName))
          res = GroupList[groupName] as ForumNewsgroup;
      }
      if (res == null)
      {
        bool exceptionOccured;
        res = GetNewsgroup(null, groupName, false, out exceptionOccured) as ForumNewsgroup;
        lock (GroupList)
        {
          if (GroupList.ContainsKey(groupName) == false)
            GroupList[groupName] = res;
        }
      }

      return res;
    }

    public override Article GetArticleById(string clientUsername, string groupName, string articleId)
    {
      var g = GetNewsgroupFromCacheOrServer(groupName) as ForumNewsgroup;
      return GetArticleById(g, articleId);
    }

    private ForumArticle GetArticleById(ForumNewsgroup g, string articleId)
    {
      if (g == null)
        throw new ApplicationException("No group provided");

      Guid? id = ForumArticle.IdToGuid(articleId);
      if (id == null) return null;

      return GetArticleByIdInternal(g, id.Value);
    }

    private ForumArticle GetArticleByIdInternal(ForumNewsgroup g, Guid id)
    {
      if (g == null)
      {
        return null;
      }

      if (UserSettings.Default.DisableArticleCache == false)
      {
        // Check if the article is in my cache...
        lock (g.Articles)
        {
          foreach (var ar in g.Articles.Values)
          {
            var fa = ar as ForumArticle;
            if ((fa != null) && (fa.MappingValue.Id == id))
              return fa;
          }
        }
      }

      ForumArticle a = _management.GetMessageById(g, id);
      if (a == null) return null;

      ConvertNewArticleFromWebService(a);

      // Only store the message if the Msg# is correct!
      if (UserSettings.Default.DisableArticleCache == false)
      {
        lock (g.Articles)
        {
          g.Articles[a.Number] = a;
        }
      }
      return a;
    }

    #region IArticleConverter

    public UsePlainTextConverters UsePlainTextConverter
    {
      get { return _converter.UsePlainTextConverter; }
      set { _converter.UsePlainTextConverter = value; }
    }


    public int AutoLineWrap
    {
      get { return _converter.AutoLineWrap; }
      set { _converter.AutoLineWrap = value; }
    }


    public bool PostsAreAlwaysFormatFlowed
    {
      get { return _converter.PostsAreAlwaysFormatFlowed; }
      set { _converter.PostsAreAlwaysFormatFlowed = value; }
    }

    public int TabAsSpace
    {
      get { return _converter.TabAsSpace; }
      set { _converter.TabAsSpace = value; }
    }

    public bool UseCodeColorizer
    {
      get { return _converter.UseCodeColorizer; }
      set { _converter.UseCodeColorizer = value; }
    }

    private readonly ArticleConverter.Converter _converter = new ArticleConverter.Converter();

    private void ConvertNewArticleFromWebService(Article a)
    {
      try
      {
        _converter.NewArticleFromWebService(a, HeaderEncoding);
      }
      catch (Exception exp)
      {
        Traces.Main_TraceEvent(TraceEventType.Error, 1, "ConvertNewArticleFromWebService failed: {0}",
                               NNTPServer.Traces.ExceptionToString(exp));
      }
    }

    private void ConvertNewArticleFromNewsClientToWebService(Article a)
    {
      try
      {
        _converter.NewArticleFromClient(a);
      }
      catch (Exception exp)
      {
        Traces.Main_TraceEvent(TraceEventType.Error, 1, "ConvertNewArticleFromNewsClientToWebService failed: {0}",
                               NNTPServer.Traces.ExceptionToString(exp));
      }
    }

    #endregion

    public override Article GetArticleByNumber(string clientUsername, string groupName, int articleNumber)
    {
      var g = GetNewsgroupFromCacheOrServer(groupName) as ForumNewsgroup;
      if (g == null) return null;
      if (UserSettings.Default.DisableArticleCache == false)
      {
        lock (g.Articles)
        {
          if (g.Articles.ContainsKey(articleNumber))
            return g.Articles[articleNumber];
        }
      }

      var a = _management.GetMessageByMsgNo(g, articleNumber);
      if (a == null) return null;

      ConvertNewArticleFromWebService(a);

      if (UserSettings.Default.DisableArticleCache == false)
      {
        lock (g.Articles)
        {
          g.Articles[a.Number] = a;
        }
      }
      return a;
    }

    public override void GetArticlesByNumberToStream(string clientUsername, string groupName, int firstArticle,
                                                     int lastArticle, Action<IList<Article>> articlesProgressAction)
    {
      // Check if the number has the correct order... some clients may sent it XOVER 234-230 instead of "XOVER 230-234"
      if (firstArticle > lastArticle)
      {
        // the numbers are in the wrong oder, so correct it...
        var tmp = firstArticle;
        firstArticle = lastArticle;
        lastArticle = tmp;
      }

      ForumNewsgroup g;
      try
      {
        g = GetNewsgroupFromCacheOrServer(groupName) as ForumNewsgroup;
        if (g == null) return;

        lock (g)
        {
          if (g.ArticlesAvailable == false)
          {
            // If we never had checked for acrticles, we first need to do this...
            var articles = _management.UpdateGroupFromWebService(g, OnProgressData, ConvertNewArticleFromWebService);
          }
        }
      }
      catch (Exception exp)
      {
        Traces.Main_TraceEvent(TraceEventType.Error, 1, NNTPServer.Traces.ExceptionToString(exp));
        return;
      }

      // Be sure we do not ask too much...
      if (firstArticle < g.FirstArticle)
        firstArticle = g.FirstArticle;
      if (lastArticle > g.LastArticle)
        lastArticle = g.LastArticle;

      for (int no = firstArticle; no <= lastArticle; no++)
      {
        Article a = null;
        if (UserSettings.Default.DisableArticleCache == false)
        {
          lock (g.Articles)
          {
            if (g.Articles.ContainsKey(no))
              a = g.Articles[no];
          }
        }
        if (a == null)
        {
          var res = _management.GetMessageByMsgNo(g, no);
          if (res != null)
          {
            a = res;
            ConvertNewArticleFromWebService(a);
            if (UserSettings.Default.DisableArticleCache == false)
            {
              lock (g.Articles)
              {
                if (g.Articles.ContainsKey(no) == false)
                  g.Articles[no] = a;
              }
            }
          }
        }
        if (a != null)
          articlesProgressAction(new [] {a});
      }
    }

    private static readonly Regex RemoveUnusedhtmlStuffRegex = new Regex(".*<body[^>]*>\r*\n*(.*)\r*\n*</\\s*body>", 
            RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static string RemoveUnsuedHtmlStuff(string text)
        {
            var m = RemoveUnusedhtmlStuffRegex.Match(text);
            if (m.Success)
            {
                return m.Groups[1].Value;
            }
            return text;
        }

        protected override void SaveArticles(string clientUsername, List<Article> articles)
        {
          throw new ApplicationException("Currently not supported!");
          //foreach (var a in articles)
          //{
          //    var g = GetNewsgroupFromCacheOrServer(a.ParentNewsgroup) as ForumNewsgroup;
          //    if (g == null)
          //        throw new ApplicationException("Newsgroup not found!");

          //    ConvertNewArticleFromNewsClientToWebService(a);

          //    if (a.ContentType.IndexOf("text/html", StringComparison.InvariantCultureIgnoreCase) >= 0)
          //    {
          //        a.Body = RemoveUnsuedHtmlStuff(a.Body);
          //    }
          //    else //if (a.ContentType.IndexOf("text/plain", StringComparison.InvariantCultureIgnoreCase) >= 0)
          //    {
          //        // It seems to be plain text, so convert it to "html"...
          //        a.Body = a.Body.Replace("\r", string.Empty);
          //        a.Body = System.Web.HttpUtility.HtmlEncode(a.Body);
          //        a.Body = a.Body.Replace("\n", "<br />");
          //    }

          //    if ((UserSettings.Default.DisableUserAgentInfo == false) && (string.IsNullOrEmpty(a.Body) == false))
          //    {
          //        a.Body = a.Body + string.Format("<a name=\"{0}_CommunityBridge\" title=\"{1} via {2}\" />", Guid.NewGuid().ToString(), a.UserAgent, Article.MyXNewsreaderString);
          //    }

          //    // Check if this is a new post or a reply:
          //    long myThreadGuid = -1;
          //    if (string.IsNullOrEmpty(a.References))
          //    {
          //      Traces.WebService_TraceEvent(TraceEventType.Verbose, 1, "CreateQuestionThread: ForumId: {0}, Subject: {1}, Content: {2}", g.GroupName, a.Subject, a.Body);
          //      // INFO: This is not suppotred!
          //      throw new ApplicationException("Creating new threads is not supported!");
          //    }
          //    else
          //    {
          //      // FIrst get the parent Message, so we can retrive the discussionId (threadId)
          //      // retrive the last reference:
          //      string[] refes = a.References.Split(' ');
          //      var res = GetArticleById(g, refes[refes.Length - 1].Trim());
          //      if (res == null)
          //        throw new ApplicationException("Parent message not found!");

          //      Traces.WebService_TraceEvent(TraceEventType.Verbose, 1, "CreateReply: ForumId: {0}, DiscussionId: {1}, ThreadId: {2}, Content: {3}", g.GroupName, res.DiscussionId, res.Guid, a.Body);

          //      // TODO:
          //      //myThreadGuid = _serviceProviders.AddMessage(res.DiscussionId, res.Guid, a.Body);
          //      throw new ApplicationException("Posting comments not yet possible!");
          //    }

          //    // Auto detect my email and username (guid):
          //    try
          //    {
          //        // Try to find the email address in the post:
          //        var m = emailFinderRegEx.Match(a.From);
          //        if (m.Success)
          //        {
          //          string userName = m.Groups[1].Value.Trim(' ', '<', '>');
          //            string email = m.Groups[3].Value;

          //            // try to find this email in the usermapping collection:
          //            bool bFound = false;
          //            lock (UserSettings.Default.UserMappings)
          //            {
          //                foreach (var um in UserSettings.Default.UserMappings)
          //                {
          //                    if (string.Equals(um.UserEmail, email, 
          //                        StringComparison.InvariantCultureIgnoreCase))
          //                    {
          //                        // Address is already known...
          //                        bFound = true;
          //                        break;
          //                    }
          //                }
          //            }
          //            if (bFound == false)
          //            {
          //                // I have not yet this email address, so find the user guid for the just posted article:
          //              // INFO: The article is not yet in the cache, so we have no Msg#!
          //                var a2 = GetArticleByIdInternal(g, myThreadGuid, null, false);
          //                if (a2 != null)
          //                {
          //                    var userGuid = a2.UserGuid;
          //                    // Now store the data in the user settings
          //                    bool bGuidFound = false;
          //                    lock (UserSettings.Default.UserMappings)
          //                    {
          //                        foreach (var um in UserSettings.Default.UserMappings)
          //                        {
          //                            if (um.Id == userGuid)
          //                            {
          //                                bGuidFound = true;
          //                                um.UserEmail = email;
          //                            }
          //                        }
          //                        if (bGuidFound == false)
          //                        {
          //                          var um = new UserMapping();
          //                          um.Id = userGuid;
          //                          um.UserEmail = email;
          //                          if ((string.IsNullOrEmpty(a2.DisplayName) == false) && (a2.DisplayName.Contains("<null>") == false))
          //                            um.UserName = a2.DisplayName;
          //                          else
          //                          {
          //                            if (string.IsNullOrEmpty(userName) == false)
          //                              um.UserName = userName;
          //                          }
          //                          if (string.IsNullOrEmpty(um.UserName) == false)
          //                            UserSettings.Default.UserMappings.Add(um);
          //                        }
          //                    }  // lock
          //                }
          //            }
          //        }
          //    }
          //    catch (Exception exp)
          //    {
          //        Traces.Main_TraceEvent(TraceEventType.Error, 1, "Error in retrieving own article: {0}", NNTPServer.Traces.ExceptionToString(exp));                    
          //    }
          //}
        }

        Regex emailFinderRegEx = new Regex(@"^(.*(\s|<))([a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)(>|s|$)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        #endregion
    }  // class ForumDataSource

    public class ForumNewsgroup : Newsgroup
    {
      internal const int DefaultMsgNumber = 1000;

      public ForumNewsgroup(NewsgroupConfigEntry config)
        : base(config.Name, 1, DefaultMsgNumber, true, DefaultMsgNumber, DateTime.Now)
      {
        _config = config;
        DisplayName = config.Name;
        Description = string.Format("Newsgroup from '{0}' with the tags: '{1}", config.Server, config.Tags);

        StackyClient = new StackyClient("2.1", "9aT4ZKsThCbBFlD5skBrEw((", "http://api.stackexchange.com", new UrlClient(), new JsonProtocol());
      }

      private NewsgroupConfigEntry _config;

      internal string Tags { get { return _config.Tags; } }
      internal string Site { get { return _config.Server; } }

      internal StackyClient StackyClient;

      /// <summary>
      /// If this is "false", then this group had never asked for articles!
      /// </summary>
      public bool ArticlesAvailable { get; set; }
    }  // class ForumNewsgroup

    public class ForumArticle : Article
    {
      public ForumArticle(ForumNewsgroup g, Mapping mapping, Question question)
        : base((int)mapping.NNTPMessageNumber)
      {
#if DEBUG
        _question = question;
#endif
        Id = GuidToId(mapping.Id);

        MappingValue = mapping;

        DateTime dt = question.CreationDate;
        //if (question.LastActivityDate != DateTime.MinValue)
        //  dt = question.LastActivityDate;
        Date = string.Format("{0} +0000", dt.ToString("ddd, d MMM yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));

        string author = null;
        if (question.Owner != null)
        {
          author = question.Owner.DisplayName;
        }
        if (string.IsNullOrEmpty(author))
          author = "Unknown <null>";

        From = author;
        DisplayName = author;

        //if (mapping.ParentId == null)
        {
          // It is the "primary" question
          string sub = string.Empty;
          if (UserSettings.Default.MessageInfos == UserSettings.MessageInfoEnum.InSignatureAndSubject)
          {
            if ((question.Tags != null) && (question.Tags.Any()))
              sub = "[" + string.Join("; ", question.Tags) + "] ";
          }
          Subject = sub + question.Title;
        }
        //else
        //{
        //  // It is a change of the question!
        //  Subject = "Re: [MODIFY] " + question.Title;

        //  References = GuidToId(mapping.ParentId.Value);
        //}

        Newsgroups = g.GroupName;
        ParentNewsgroup = Newsgroups;
        Path = "LOCALHOST.StackAppBridge";

        var mhStr = new StringBuilder();
        mhStr.Append("<br/>-----<br/>");
        mhStr.Append("<strong>QUESTION</strong>");
        if (question.Tags != null)
          mhStr.Append("<br/>Tags: <strong>" + string.Join(";", question.Tags) + "</strong>");
        if (string.IsNullOrEmpty(question.Link) == false)
          mhStr.AppendFormat("<br/>Link: <a href='{0}'>{0}</a>", question.Link);
        if (question.DownVoteCount != 0)
          mhStr.AppendFormat("<br/>DownVote#: {0}", question.DownVoteCount);
        if (question.UpVoteCount != 0)
          mhStr.AppendFormat("<br/>UpVote#: {0}", question.UpVoteCount);
        if (question.ClosedDate != DateTime.MinValue)
          mhStr.AppendFormat("<br/>Closed: {0} +0000 ({1})", 
            question.ClosedDate.ToString("ddd, d MMM yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), 
            question.ClosedReason);
        mhStr.Append("<br/>-----<br/>");

        Body = question.Body + mhStr;
      }

      public ForumArticle(ForumNewsgroup g, Mapping mapping, Comment comment)
        : base((int)mapping.NNTPMessageNumber)
      {
#if DEBUG
        _comment = comment;
#endif
        Id = GuidToId(mapping.Id);

        MappingValue = mapping;

        DateTime dt = comment.CreationDate;
        //if (question.LastActivityDate != DateTime.MinValue)
        //  dt = question.LastActivityDate;
        Date = string.Format("{0} +0000", dt.ToString("ddd, d MMM yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));

        string author = null;
        if (comment.Owner != null)
        {
          author = comment.Owner.DisplayName;
        }
        if (string.IsNullOrEmpty(author))
          author = "Unknown <null>";

        From = author;
        DisplayName = author;

        string sub = string.Empty;
        if (UserSettings.Default.MessageInfos == UserSettings.MessageInfoEnum.InSignatureAndSubject)
        {
          sub = "[COMMENT] ";
        }
        Subject = "Re: " + sub + mapping.Title;
        if (mapping.ParentId != null)
          References = GuidToId(mapping.ParentId.Value);

        Newsgroups = g.GroupName;
        ParentNewsgroup = Newsgroups;
        Path = "LOCALHOST.StackAppBridge";

        var mhStr = new StringBuilder();
        mhStr.Append("<br/>-----<br/>");
        mhStr.Append("<strong>COMMENT</strong><br/>");
        mhStr.AppendFormat("<br/>Link: <a href='{0}'>{0}</a>", comment.Link);
        if (comment.Score != 0)
          mhStr.AppendFormat("<br/>Score#: {0}", comment.Score);
        mhStr.Append("<br/>-----<br/>");

        Body = comment.Body + mhStr;
      }

      public ForumArticle(ForumNewsgroup g, Mapping mapping, Answer answer)
        : base((int)mapping.NNTPMessageNumber)
      {
#if DEBUG
        _answer = answer;
#endif
        Id = GuidToId(mapping.Id);

        MappingValue = mapping;

        DateTime dt = answer.CreationDate;
        if (answer.LastActivityDate != DateTime.MinValue)
          dt = answer.LastActivityDate;
        Date = string.Format("{0} +0000", dt.ToString("ddd, d MMM yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));

        string author = null;
        if (answer.Owner != null)
        {
          author = answer.Owner.DisplayName;
        }
        if (string.IsNullOrEmpty(author))
          author = "Unknown <null>";

        From = author;
        DisplayName = author;

        string sub = string.Empty;
        if (UserSettings.Default.MessageInfos == UserSettings.MessageInfoEnum.InSignatureAndSubject)
        {
          sub = "[ANSWER] ";
        }

        Subject = "Re: " + sub + mapping.Title;
        if (mapping.ParentId != null)
          References = GuidToId(mapping.ParentId.Value);

        Newsgroups = g.GroupName;
        ParentNewsgroup = Newsgroups;
        Path = "LOCALHOST.StackAppBridge";

        var mhStr = new StringBuilder();
        mhStr.Append("<br/>-----<br/>");
        mhStr.Append("<strong>ANSWER</strong>");
        mhStr.AppendFormat("<br/>Link: <a href='{0}'>{0}</a>", answer.Link);
        if (answer.Score != 0)
          mhStr.AppendFormat("<br/>Score#: {0}", answer.Score);
        mhStr.Append("<br/>-----<br/>");

        Body = answer.Body + mhStr;
      }

#if DEBUG
      private Question _question;
      private Comment _comment;
      private Answer _answer;
#endif

      internal void UpdateParentId()
      {
        if (MappingValue.ParentId != null)
          References = GuidToId(MappingValue.ParentId.Value);
      }

      public Mapping MappingValue;

      // The "-" is a valid character in the messageId field:
      // http://www.w3.org/Protocols/rfc1036/rfc1036.html#z2
      public static string GuidToId(Guid id)
      {
        return "<" + id.ToString("D", System.Globalization.CultureInfo.InvariantCulture) + "$stackappnntpbridge.codeplex.com>";
      }


        public static Guid? IdToGuid(string id)
        {
          if (id == null) return null;
          if (id.StartsWith("<") == false) return null;
          id = id.Trim('<', '>');
          var parts = id.Split('$');

          // The first part is always the id:
          Guid idVal;
          if (Guid.TryParse(parts[0], out idVal) == false)
            return null;

          return idVal;
        }
    }



  /// <summary>
  /// This class is responsible for providing the corret message number for a forum / tread / message
  /// </summary>
  /// <remarks>
  /// The concept is as follows:
  /// - At the beginning, the max. Msgä is 1000
  /// - If the first message is going to be retrived, then the last x days of messages are retrived from the forum
  /// 
  /// 
  /// - Last message number (Msg#) of the group -
  /// There must be a difference between the first time and the later requests.
  /// The first time, we need to find out how many messages we want to retrive from the web-service.
  /// The logic will be:
  /// - Retrive the last xxx threads via "GetThreadListByForumId(id, locale, metadataFilter[], threadfilter[], threadSortOrder?, sortDirection, startRow, maxRows, optional)"
  ///   - With this, we have a list of the last xxx threads with the corresponding "ReplyCountField" from the ThreadStatistics
  ///   - Then we start the Msg# with "10000" (constant)
  ///   - Then we calculate the last Msg# by "threads + (foreach += thread.ReplyCount) and we also save the Msg# for each thread
  ///   - Alternatively we request the whole list of replies to each thread and store the id
  ///   - After we have all messages, we sort it by date and generate the Msg#
  /// </remarks>
  internal class MsgNumberManagement
  {
    public MsgNumberManagement(string basePath)
    {
      _baseDir = System.IO.Path.Combine(basePath, "Data");
      if (System.IO.Directory.Exists(_baseDir) == false)
      {
        System.IO.Directory.CreateDirectory(_baseDir);
      }

      _db = new LocalDbAccess(_baseDir);
    }

    private readonly LocalDbAccess _db;
    private readonly string _baseDir;

    /// <summary>
    /// Sets the max. Msg# and the number of messages for the given forum. It returns <c>false</c> if there are no messages stored for this forum.
    /// </summary>
    /// <param name="group"></param>
    /// <returns></returns>
    public bool GetMaxMessageNumber(ForumNewsgroup group)
    {
      lock (group)
      {
        // false: prevent the database from being created if it does not yet exist
        using (var con = _db.CreateConnection(group.GroupName, false))
        {
          if (con == null)
          {
            group.ArticlesAvailable = false;
            return false;
          }
          if (con.Mappings.Any() == false)
          {
            group.ArticlesAvailable = false;
            return false;
          }
          long min = con.Mappings.Min(p => p.NNTPMessageNumber);
          long max = con.Mappings.Max(p => p.NNTPMessageNumber);
          group.FirstArticle = (int) min;
          group.LastArticle = (int) max;
          group.NumberOfArticles = (int) (max - min);
          group.ArticlesAvailable = true;
          return true;
        }
      }
    }

    private DateTime? GetLastActivityDateForQuestions(ForumNewsgroup group)
    {
      using (var con = _db.CreateConnection(group.GroupName))
      {
        if (con.Mappings.Any(p => p.LastActivityDate != null) == false)
          return null;
        var dt = con.Mappings.Where(p => p.LastActivityDate != null).Max(p => p.LastActivityDate.Value);
        return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond, DateTimeKind.Utc);
      }
    }

    /// <summary>
    /// Updates the group from the web service.
    /// </summary>
    /// <param name="group"></param>
    /// <param name="progress"></param>
    /// <param name="articleConverter"></param>
    /// <remarks>
    /// This method must be multi-threaded save, because it might be accessed form a client 
    /// in different threads if for example the server is too slow...
    /// </remarks>
    /// <returns>It returns (some of the) new articles</returns>
    public IEnumerable<ForumArticle> UpdateGroupFromWebService(ForumNewsgroup group, Action<string> progress, Action<ForumArticle> articleConverter)
    {
      // Lock on the group...
      lock (group)
      {
        // rersult list...
        var articles = new List<ForumArticle>();

        // First get the Msg# from the local mapping table:
        GetMaxMessageNumber(group);

        DateTime? lastActivityDateTime = GetLastActivityDateForQuestions(group);

        if ((group.ArticlesAvailable == false) || (lastActivityDateTime == null))
        {
          // It is the first time, we are asking articles from this newsgroup
          // How many should be fetch?

          int page = 1;
          bool hasMore;
          var newArticles = new List<ForumArticle>();
          do
          {
            IEnumerable<ForumArticle> articlesForPage =
              GetQuestionsWithCommentsAndAnswers(group, page, null, out hasMore);
            newArticles.AddRange(articlesForPage);
            page++;
          } while ((hasMore) && (newArticles.Count < 500)); // INFO: Max 500 for the first query...

          ProcessNewArticles(group, newArticles, articles, group.ArticlesAvailable == false);

          // Aktualisiere die Msg-Numbers
          GetMaxMessageNumber(group);
        }
        else
        {
          // Es gibt schon Einträge, also frage nach der letzten Abfrage:
          int page2 = 1;
          bool hasMore2;
          do
          {
            IEnumerable<ForumArticle> newArticles = GetQuestionsWithCommentsAndAnswers(group, page2,
                                                                                       lastActivityDateTime,
                                                                                       out hasMore2);
            ProcessNewArticles(group, newArticles, articles);
            page2++;
          } while (hasMore2);

          // Aktualisiere die Msg-Numbers
          GetMaxMessageNumber(group);
        }

        if (UserSettings.Default.DisableArticleCache == false)
        {
          foreach (var a in articles)
          {
            if (articleConverter != null)
            {
              articleConverter(a);
              //ConvertNewArticleFromWebService(a);
            }
            lock (group.Articles)
            {
              group.Articles[a.Number] = a;
            }
          }
        }

        return articles;
      } // lock
    }

    private void ProcessNewArticles(ForumNewsgroup group, IEnumerable<ForumArticle> newArticles, List<ForumArticle> articles, bool firstTime = false)
    {
      if (newArticles.Any() == false)
        return;
      // Now, create a goood NNTP# and save it to the database...
      using (var con = _db.CreateConnection(group.GroupName))
      {
        int maxNr = group.LastArticle;
        foreach (ForumArticle article in newArticles)
        {
          // Suche, ob der Artikel schon vorhanden ist...
          if (con.Mappings.Any(p => p.PostId == article.MappingValue.PostId))
          {
            // TODO: Den Artikel gibt es schon...
            Traces.WebService_TraceEvent(TraceEventType.Information, 1, "  Article-NotAdded: {0}", article.MappingValue.Id);
          }
          else
          {
            if (article.MappingValue.ParentPostId != 0)
            {
              Guid? parentId = null;
              // First: Check the currently added entries...
              ForumArticle parentArticle = articles.FirstOrDefault(p => p.MappingValue.PostId == article.MappingValue.ParentPostId);
              if (parentArticle != null)
              {
                parentId = parentArticle.MappingValue.Id;
              }
              if (parentId == null)
              {
                // Try to find the ID in the database
                var parent = con.Mappings.FirstOrDefault(p => p.PostId == article.MappingValue.ParentPostId);
                if (parent != null)
                {
                  parentId = parent.Id;
                }
              }
              if (parentId != null)
              {
                article.MappingValue.ParentId = parentId.Value;
                article.UpdateParentId();
              }
              else
              {
                // Parent not found!
              }
            }
            article.Number = ++maxNr;
            article.MappingValue.NNTPMessageNumber = article.Number;
            con.Mappings.AddObject(article.MappingValue);

            articles.Add(article);
          }
        }

        if (firstTime)
        {
          // If it was the first time, then we need to use a different numbering schema...
          // We need to number fromlower numbers to higher numbers...
          int idx = ForumNewsgroup.DefaultMsgNumber - articles.Count;
          if (idx <= 0)
            idx = 1;
          foreach (ForumArticle article in articles)
          {
            article.Number = idx;
            article.MappingValue.NNTPMessageNumber = article.Number;
            idx++;
          }
        }

        con.SaveChanges(SaveOptions.None);
      }
    }

    // Default + answer.comments;answer.link;answer.body;question.answers;question.comments;question.body;comment.link;comment.body
    internal const string QuestionFilterNameWithBody = "!0ZPuz7ZFJF)YU8rYJHAppml3Z";

    internal const int PostTypeQuestion = 0;
    internal const int PostTypeComment = 1;
    internal const int PostTypeAnswer = 2;

    private IEnumerable<ForumArticle> GetQuestionsWithCommentsAndAnswers(ForumNewsgroup group, int page, DateTime? lastActivityFrom, out bool hasMore, int pageSize = 50)
    {
      hasMore = false;
      var result = new List<ForumArticle>();

      // The "LastActivityDate" is only set in the "Question" mapping entry,.
      // With this value, we can later determine the timespan for querying the last activities..

      Traces.WebService_TraceEvent(TraceEventType.Information, 1, "Update questions ({1}): {0}", lastActivityFrom == null ? "(null)" : lastActivityFrom.Value.ToString("s"), group.GroupName);

      IPagedList<Question> res = group.StackyClient.GetQuestions(QuestionSort.Activity, SortDirection.Descending, page, pageSize,
                                      filter: QuestionFilterNameWithBody,
                                      tags:group.Tags,
                                      site_:group.Site,
                                      //fromDate: lastActivityFrom);  // "fromDate" is always related to "CreationDate"!!!
                                      min_: (long?)Stacky.DateHelper.ToUnixTime(lastActivityFrom.Value));  // And "min" is always related to "sort" value!

      hasMore = res.HasMore;

      foreach (Question question in res)
      {
        Traces.WebService_TraceEvent(TraceEventType.Information, 1, "Question: {0} ({1})", question.Id, question.LastActivityDate.ToString("s"));

        // First, create the mapping-Entry:
        var map = new Mapping();
        map.PostId = question.Id;
        map.Id = Guid.NewGuid();
        map.PostType = PostTypeQuestion;
        map.Title = question.Title;
        if (question.LastActivityDate != DateTime.MinValue)
          map.LastActivityDate = question.LastActivityDate;
        else
          map.LastActivityDate = question.CreationDate;

        var q = new ForumArticle(group, map, question);
        result.Add(q);

        // Now also add all comments:
        if (question.Comments != null)
        {
          foreach (Comment comment in question.Comments)
          {
            Traces.WebService_TraceEvent(TraceEventType.Information, 1, "  Comment: qid:{0} {1}", question.Id, comment.Id);
            // First, create the mapping-Entry:
            var mapc = new Mapping();
            mapc.PostId = comment.Id;
            mapc.Id = Guid.NewGuid();
            mapc.ParentPostId = map.PostId;
            //mapc.ParentId = map.Id;
            mapc.PostType = PostTypeComment;
            mapc.Title = question.Title;

            var qc = new ForumArticle(group, mapc, comment);
            result.Add(qc);
          }
        }

        // Now also add all answers
        if (question.Comments != null)
        {
          foreach (Answer answer in question.Answers)
          {
            Traces.WebService_TraceEvent(TraceEventType.Information, 1, "  Answer: qid:{0} {1} ({2})", question.Id, answer.Id, answer.LastActivityDate.ToString("s"));
            // First, create the mapping-Entry:
            var mapa = new Mapping();
            mapa.PostId = answer.Id;
            mapa.Id = Guid.NewGuid();
            mapa.ParentPostId = map.PostId;
            //mapa.ParentId = map.Id;
            mapa.PostType = PostTypeAnswer;
            mapa.Title = question.Title;

            var ac = new ForumArticle(group, mapa, answer);
            result.Add(ac);

            // Now also add all comments from this answer
            if (answer.Comments != null)
            {
              foreach (Comment comment2 in answer.Comments)
              {
                Traces.WebService_TraceEvent(TraceEventType.Information, 1, "    Comment: qid:{0} aid:{1} {2}", question.Id, answer.Id, comment2.Id);
                // First, create the mapping-Entry:
                var mapc2 = new Mapping();
                mapc2.PostId = comment2.Id;
                mapc2.Id = Guid.NewGuid();
                mapc2.ParentId = mapa.Id;
                mapc2.PostType = PostTypeComment;
                mapc2.Title = question.Title;

                var qac = new ForumArticle(group, mapc2, comment2);
                result.Add(qac);
              }
            }
          }
        }
      }

      return result;
    }

    public ForumArticle GetMessageById(ForumNewsgroup forumNewsgroup, Guid id)
    {
      Mapping map;
      lock (forumNewsgroup)
      {
        using (var con = _db.CreateConnection(forumNewsgroup.GroupName))
        {
            map = con.Mappings.FirstOrDefault(p => p.Id == id);
        }
      }
      if (map == null)
      {
        return null;
      }
      return InternalGetMsgById(forumNewsgroup, map);
    }

    public ForumArticle GetMessageByMsgNo(ForumNewsgroup forumNewsgroup, int articleNumber)
    {
      Mapping map;
      lock (forumNewsgroup)
      {
        using (var con = _db.CreateConnection(forumNewsgroup.GroupName))
        {
          map = con.Mappings.FirstOrDefault(p => p.NNTPMessageNumber == articleNumber);
        }
      }
      if (map == null)
      {
        return null;
      }
      return InternalGetMsgById(forumNewsgroup, map);
    }

    private ForumArticle InternalGetMsgById(ForumNewsgroup group, Mapping map)
    {
      switch (map.PostType)
      {
        case PostTypeQuestion:
          {
            IPagedList<Question> result = group.StackyClient.GetQuestions(new[] {map.PostId}, _filter: QuestionFilterNameWithBody, site_: group.Site);
            var q = result.FirstOrDefault();
            Traces.WebService_TraceEvent(TraceEventType.Information, 1, "GetQuestion: id:{0}", map.PostId);
            if (q != null)
            {
              return new ForumArticle(group, map, q);
            }
            return null;
          }
        case PostTypeAnswer:
          {
            IPagedList<Answer> result = group.StackyClient.GetAnswers(new[] {map.PostId}, filter_: QuestionFilterNameWithBody, site_: group.Site);
            var a = result.FirstOrDefault();
            Traces.WebService_TraceEvent(TraceEventType.Information, 1, "GetAnswer: id:{0}", map.PostId);
            if (a != null)
            {
              return new ForumArticle(group, map, a);
            }
            return null;
          }
        case PostTypeComment:
          {
            IPagedList<Comment> result = group.StackyClient.GetComments(new[] { map.PostId }, filter_: QuestionFilterNameWithBody, site_: group.Site);
            var c = result.FirstOrDefault();
            Traces.WebService_TraceEvent(TraceEventType.Information, 1, "GetComment: id:{0}", map.PostId);
            if (c != null)
            {
              return new ForumArticle(group, map, c);
            }
            return null;
          }
      }
      return null;
    }

    //private static void GetMetaDataString(int deep, MetaData metaData, StringBuilder result, List<string> subForumNames, string parentName)
    //{
    //  string myNewsGroupName = parentName;
    //  if (metaData.MetaDataTypes.Any(p => p.Id == MetaDataInfo.MetaValue))
    //  {
    //    myNewsGroupName += "." + metaData.ShortName;
    //    subForumNames.Add(myNewsGroupName);
    //  }
    //  var deepStr = new string(' ', deep * 2);
    //  result.AppendFormat("{0} {1:00} Short: '{2}' Display: '{3}' Locale: '{4}, Id: {5}<br/>", deepStr, deep, metaData.ShortName, metaData.DisplayName, metaData.LocaleName, metaData.Id);
    //  foreach (var types in metaData.MetaDataTypes)
    //  {
    //    result.AppendFormat("{0}  {1:00} Type: {2}, Id: {3}<br/>", deepStr, deep, types.ShortName, types.Id);
    //  }
    //  foreach (var childMetaData in metaData.ChildMetaData)
    //  {
    //    GetMetaDataString(deep + 1, childMetaData, result, subForumNames, myNewsGroupName);
    //  }
    //}

    //public void SaveGroupFilterData(ForumNewsgroup g)
    //{
    //  if ((g.MetaDataInfo != null) && (g.MetaDataInfo.FilterIds != null))
    //  {
    //    var sb = new StringBuilder();
    //    foreach (var fd in g.MetaDataInfo.FilterIds)
    //    {
    //      if (sb.Length > 0) sb.Append(":");
    //      sb.Append(fd.ToString("D"));
    //    }
    //    string fn = IniFile(g.BaseGroupName, "FilterData.ini");
    //    IniHelper.SetString(g.GroupName.ToLowerInvariant(), "Name", g.MetaDataInfo.Name, fn);
    //    IniHelper.SetString(g.GroupName.ToLowerInvariant(), "FilterData", sb.ToString(), fn);
    //  }
    //}
    //public MetaDataInfo LoadGroupFilterData(string baseGroupName, string groupName)
    //{
    //  string fn = IniFile(baseGroupName, "FilterData.ini");
    //  string s = IniHelper.GetString(groupName.ToLowerInvariant(), "FilterData", fn);
    //  if (string.IsNullOrEmpty(s))
    //    return null;
    //  string[] ids = s.Split(':');
    //  Guid[] guids = new Guid[ids.Length];
    //  for (int i = 0; i < ids.Length; i++)
    //  {
    //    guids[i] = Guid.ParseExact(ids[i], "D");
    //  }
    //  var md = new MetaDataInfo();
    //  md.FilterIds = guids;
    //  md.Name = IniHelper.GetString(groupName.ToLowerInvariant(), "Name", fn);
    //  return md;
    //}

    //public void SaveAllMetaData(ForumNewsgroup g)
    //{
    //  if (g.UniqueInfos != null)
    //  {
    //    string fn = IniFile(g.BaseGroupName, "AllMetaData.ini");
    //    foreach (var md in g.UniqueInfos)
    //    {
    //      IniHelper.SetString(md.FilterIds[0].ToString("D"), "Name", md.Name, fn);
    //    }
    //  }
    //}
    //public IEnumerable<MetaDataInfo> LoadAllMetaData(QnAClient provider, Guid forumId, string locale, string baseGroupName, bool forceUpdate = false)
    //{
    //  var infos = new List<MetaDataInfo>();
    //  string fn = IniFile(baseGroupName, "AllMetaData.ini");
    //  string[] sections = IniHelper.GetSectionNamesFromIni(fn);
    //  if (sections != null)
    //  {
    //    foreach (var s in sections)
    //    {
    //      var md = new MetaDataInfo();
    //      md.FilterIds = new[] {Guid.ParseExact(s, "D")};
    //      md.Name = IniHelper.GetString(s, "Name", fn);
    //      infos.Add(md);
    //    }
    //  }
    //  if ((provider != null) && ((infos.Count <= 0) || forceUpdate) )
    //  {
    //    var result = provider.GetMetaDataListByForumId(forumId, locale);
    //    var uniqueInfos = new List<MetaDataInfo>();
    //    if (result != null)
    //    {
    //      foreach (var metaData in result)
    //      {
    //        MetaDataInfo.GetMetaDataInfos(metaData, uniqueInfos, null);
    //      }
    //    }
    //    infos = uniqueInfos.ToList();
    //  }

    //  return infos;
    //}
  } // class MsgNumberManagement


  public class NewsgroupConfigEntryCollection : List<NewsgroupConfigEntry> 
  {
    public NewsgroupConfigEntryCollection Clone()
    {
      var c = new NewsgroupConfigEntryCollection();
      c.AddRange(this.Select(entry => new NewsgroupConfigEntry(entry)));
      return c;
    }
  }

  public class NewsgroupConfig : ICloneable
  {
    public NewsgroupConfig()
    {}

    public NewsgroupConfig(IEnumerable<NewsgroupConfigEntry> entries)
    {
      _newsgroups = new NewsgroupConfigEntryCollection();
      _newsgroups.AddRange(entries.Select(entry => new NewsgroupConfigEntry(entry)));
    }

    private readonly NewsgroupConfigEntryCollection _newsgroups = new NewsgroupConfigEntryCollection();
    public NewsgroupConfigEntryCollection Newsgroups
    {
      get { return _newsgroups; }
    }

    internal static NewsgroupConfig Default
    {
      get
      {
        var cfg = new NewsgroupConfig();
        cfg.Newsgroups.Add(new NewsgroupConfigEntry { Name = "StackOverflow.CSharp", Server = "stackoverflow", Tags = "c#" });
        cfg.Newsgroups.Add(new NewsgroupConfigEntry { Name = "StackOverflow.Windows", Server = "stackoverflow", Tags = "windows" });
        return cfg;
      }
    }

    #region Load/Save
    public void Save(string filename)
    {
      try
      {
        var path = Path.GetDirectoryName(filename);
        if (Directory.Exists(path) == false)
          Directory.CreateDirectory(path);
        var ser = new XmlSerializer(typeof(NewsgroupConfig));
        using (var sw = new StreamWriter(filename))
        {
          ser.Serialize(sw, this);
        }
      }
      catch (Exception exp)
      {
        Traces.Main_TraceEvent(TraceEventType.Critical, 1, "Error while serializing NewsgroupConfig: {0}", NNTPServer.Traces.ExceptionToString(exp));
      }
    }

    public static NewsgroupConfig Load(string fileName)
    {
      try
      {
        if (Directory.Exists(Path.GetDirectoryName(fileName)) == false)
          return Default;
        if (File.Exists(fileName) == false)
          return Default;

        var ser = new XmlSerializer(typeof(NewsgroupConfig));
        using (var sr = new StreamReader(fileName))
        {
          var res = ser.Deserialize(sr) as NewsgroupConfig;
          return res;
        }
      }
      catch (Exception exp)
      {
        Traces.Main_TraceEvent(TraceEventType.Critical, 1, "Error while deserializing NewsgroupConfig: {0}\r\n{1}", fileName, NNTPServer.Traces.ExceptionToString(exp));
      }
      return Default;
    }
    #endregion

    public object Clone()
    {
      var c = new NewsgroupConfig();
      foreach (NewsgroupConfigEntry entry in _newsgroups)
      {
        c.Newsgroups.Add(new NewsgroupConfigEntry(entry));
      }
      return c;
    }
  }
  public class NewsgroupConfigEntry
  {
    public NewsgroupConfigEntry()
    {}
    public NewsgroupConfigEntry(NewsgroupConfigEntry entry)
    {
      Name = entry.Name;
      Server = entry.Server;
      Tags = entry.Tags;
    }
    public string Name { get; set; }
    public string Server { get; set; }
    public string Tags { get; set; }
  }
}
