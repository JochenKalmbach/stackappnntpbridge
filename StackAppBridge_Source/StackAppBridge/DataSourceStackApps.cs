using System;
using System.Collections.Generic;
using System.Data.Objects;
using System.Diagnostics;
using System.Globalization;
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

    public DataSourceStackApps(string accessToken)
    {
      _accessToken = accessToken;
      _management = new MsgNumberManagement(UserSettings.Default.BasePath, _accessToken);

      ClearCache();
    }

    private string _accessToken;

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
          if (GroupList.ContainsKey(entry.Name))
          {
            // Ignore group with already existing name...
            continue;
          }

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

        // Add special "Inbox" newsgroup
        AddStackExchangeInboxGroup();

        if (res)
          SetNewsgroupCacheValid();
      } // lock

      return res;
    }

    private void AddStackExchangeInboxGroup()
    {
      // Support of Inbox-Items and Notifications...
      if (string.IsNullOrEmpty(_accessToken))
        return;

      if (UserSettings.Default.ShowInboxAndNotifications)
      {
        lock (GroupList)
        {
          // Detect all Sites...
          var sites = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
          foreach (ForumNewsgroup g in GroupList.Values)
          {
            if ( (sites.ContainsKey(g.Site) == false) && (string.IsNullOrEmpty(g.Site) == false))
              sites.Add(g.Site, null);
         }

          if (sites.Keys.Any())
          {
            var inboxGroup = new ForumNewsgroup(sites.Keys);
            if (GroupList.ContainsKey(inboxGroup.GroupName) == false)
            {
              GroupList.Add(inboxGroup.GroupName, inboxGroup);
            }
          }
        }
      }
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

      if (g.IsInboxGroup)
      {
        return _management.GetInboxItemFromId(g, articleId);
      }

      long? id = ForumArticle.IdToPostId(articleId);
      if (id == null) return null;

      return GetArticleByIdInternal(g, id.Value);
    }

    private ForumArticle GetArticleByIdInternal(ForumNewsgroup g, long postId)
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
            if ((fa != null) && (fa.MappingValue.PostId == postId))
              return fa;
          }
        }
      }

      ForumArticle art = _management.GetMessageById(g, postId);

      ConvertNewArticleFromWebService(art);

      // Only store the message if the Msg# is correct!
      if (UserSettings.Default.DisableArticleCache == false)
      {
        lock (g.Articles)
        {
          g.Articles[art.Number] = art;
        }
      }
      return art;
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

      if (g.IsInboxGroup)
      {
        return _management.GetInboxItemFromNumber(g, articleNumber);
      }

      IEnumerable<ForumArticle> a = _management.GetMessageStreamByMsgNo(g, new[] { articleNumber });
      if ((a == null) || (a.Any() == false)) return null;
      ForumArticle art = a.First();

      ConvertNewArticleFromWebService(art);

      if (UserSettings.Default.DisableArticleCache == false)
      {
        lock (g.Articles)
        {
          g.Articles[art.Number] = art;
        }
      }
      return art;
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
            _management.UpdateGroupFromWebService(g, OnProgressData, ConvertNewArticleFromWebService);
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


      if (g.IsInboxGroup)
      {
        for (int no = firstArticle; no <= lastArticle; no++)
        {
          Article a = null;
          // Check if the article is in the cache...
          if (UserSettings.Default.DisableArticleCache == false)
          {
            lock (g.Articles)
            {
              if (g.Articles.ContainsKey(no))
                a = g.Articles[no];
            }
          }

          if (a != null)
          {
            articlesProgressAction(new[] {a});
          }
          else
          {
            // Read article...
            var article = _management.GetInboxItemFromNumber(g, no);
            if (article != null)
            {
              ConvertNewArticleFromWebService(article);
              if (UserSettings.Default.DisableArticleCache == false)
              {
                lock (g.Articles)
                {
                  if (g.Articles.ContainsKey(article.Number) == false)
                    g.Articles[article.Number] = article;
                }
              }
              // output the now fetched articles...
              articlesProgressAction(new[] {article});
            }
          }
        }  // for
        return;
      }


      var missingArticles = new List<int>();
      for (int no = firstArticle; no <= lastArticle; no++)
      {
        Article a = null;
        // Check if the article is in the cache...
        if (UserSettings.Default.DisableArticleCache == false)
        {
          lock (g.Articles)
          {
            if (g.Articles.ContainsKey(no))
              a = g.Articles[no];
          }
        }

        bool flushMissingList = false;
        if (a != null)
          flushMissingList = true;  // now there is again an article available, so flush the previous articles...
        if (no == lastArticle)
          flushMissingList = true;  // if it is the last article, then we need to flush our missing list
        if (missingArticles.Count >= 95)  // limit is 100  // If we reached a limit of 95, we need to query for the articles...
          flushMissingList = true;

        if (a == null)
          missingArticles.Add(no);

        if (flushMissingList)
        {
          if (missingArticles.Count > 0)
          {
            // First process the missing articles...
            var articles = _management.GetMessageStreamByMsgNo(g, missingArticles);
            foreach (Article article in articles)
            {
              ConvertNewArticleFromWebService(article);
              if (UserSettings.Default.DisableArticleCache == false)
              {
                lock (g.Articles)
                {
                  if (g.Articles.ContainsKey(article.Number) == false)
                    g.Articles[article.Number] = article;
                }
              }
              // output the now fetched articles...
              articlesProgressAction(new[] {article});
            }
            missingArticles.Clear();
          }
        }

        // if there was an article available, then output this article also...
        if (a != null)
          articlesProgressAction(new[] { a });

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

          // TODO: Leave comments to a question or answer (currently this is the only possible operation!):
          //       /posts/{id}/comments/add 


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

      private const string apiKey = "9aT4ZKsThCbBFlD5skBrEw((";

      public ForumNewsgroup(NewsgroupConfigEntry config)
        : base(config.Name, 1, DefaultMsgNumber, false, DefaultMsgNumber, DateTime.Now)
      {
        _config = config;
        DisplayName = config.Name;
        Description = string.Format("Newsgroup from '{0}' with the tags: '{1}", config.Server, config.Tags);

        StackyClient = new StackyClient("2.1", apiKey, "https://api.stackexchange.com", new UrlClient(), new JsonProtocol());
      }

      /// <summary>
      /// Special constructor for "Inbox" and "Notification" group!
      /// </summary>
      public ForumNewsgroup(IEnumerable<string> inboxSites)
        : base("StackApps.InboxAndNotifications", 1, DefaultMsgNumber, false, DefaultMsgNumber, DateTime.Now)
      {
        IsInboxGroup = true;
        InboxSites = inboxSites.ToArray();
        DisplayName = this.GroupName;
        Description = string.Format("Inbox and notification group");

        StackyClient = new StackyClient("2.1", apiKey, "https://api.stackexchange.com", new UrlClient(), new JsonProtocol());
      }

      internal bool IsInboxGroup;
      internal string[] InboxSites;

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
        Id = PostIdToId(g.Site, mapping.PostId, mapping.CreatedDate);

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
        Id = PostIdToId(g.Site, mapping.PostId, mapping.CreatedDate);

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
        if (mapping.ParentPostId != null)
          References = PostIdToId(g.Site, mapping.ParentPostId.Value, mapping.ParentCreatedDate);

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
        Id = PostIdToId(g.Site, mapping.PostId, mapping.CreatedDate);

        MappingValue = mapping;

        DateTime dt = answer.CreationDate;
        //if (answer.LastActivityDate != DateTime.MinValue)
        //  dt = answer.LastActivityDate;
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
        if (mapping.ParentPostId != null)
          References = PostIdToId(g.Site, mapping.ParentPostId.Value, mapping.ParentCreatedDate);

        Newsgroups = g.GroupName;
        ParentNewsgroup = Newsgroups;
        Path = "LOCALHOST.StackAppBridge";

        var mhStr = new StringBuilder();
        mhStr.Append("<br/>-----<br/>");
        mhStr.Append("<strong>ANSWER</strong>");
        mhStr.AppendFormat("<br/>Link: <a href='{0}'>{0}</a>", answer.Link);
        if (answer.Score != 0)
          mhStr.AppendFormat("<br/>Score#: {0}", answer.Score);
        if (answer.Accepted)
          mhStr.Append("<br/><strong>Accepted</strong>");
        if (answer.UpVoteCount != 0)
          mhStr.AppendFormat("<br/>UpVote#: {0}", answer.UpVoteCount);
        if (answer.DownVoteCount != 0)
          mhStr.AppendFormat("<br/>DownVote#: {0}", answer.DownVoteCount);
        if (answer.FavoriteCount != 0)
          mhStr.AppendFormat("<br/>Favorite#: {0}", answer.FavoriteCount);
        if (answer.LockedDate != DateTime.MinValue)
          mhStr.AppendFormat("<br/>Locked: {0:yyyy-MM-dd HH:mm:ss}", answer.LockedDate);
        mhStr.Append("<br/>-----<br/>");

        Body = answer.Body + mhStr;
      }

#if DEBUG
      private Question _question;
      private Comment _comment;
      private Answer _answer;
      private InboxItem _inboxItem;
      private Notification _notification;
#endif


      public ForumArticle(ForumNewsgroup g, Mapping mapping, Notification notify)
        : base((int)mapping.NNTPMessageNumber)
      {
#if DEBUG
        _notification = notify;
#endif
        Id = MappingToInboxId(mapping);

        MappingValue = mapping;

        DateTime dt = notify.CreationDate;
        Date = string.Format("{0} +0000", dt.ToString("ddd, d MMM yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));

        const string author = "Notification";
        From = author;
        DisplayName = author;

        Subject = mapping.Title;

        Newsgroups = g.GroupName;
        ParentNewsgroup = Newsgroups;
        Path = "LOCALHOST.StackAppBridge";

        var mhStr = new StringBuilder();
        mhStr.Append("<br/>-----<br/>");
        mhStr.Append("<strong>NOTIFICATION: ");
        mhStr.Append(notify.NotificationType);
        mhStr.Append("</strong>");
        if (notify.PostId != 0)
          mhStr.AppendFormat("<br/>PostId: {0}", notify.PostId);
        if (notify.CreationDate != DateTime.MinValue)
          mhStr.AppendFormat("<br/>Created: {0:yyyy-MM-dd HH:mm:ss}", notify.CreationDate);
        mhStr.Append("<br/>-----<br/>");

        Body = notify.Body + mhStr;
      }

      public ForumArticle(ForumNewsgroup g, Mapping mapping, InboxItem inboxItem)
        : base((int)mapping.NNTPMessageNumber)
      {
#if DEBUG
        _inboxItem = inboxItem;
#endif
        Id = MappingToInboxId(mapping);

        MappingValue = mapping;

        DateTime dt = inboxItem.CreationDate;
        Date = string.Format("{0} +0000", dt.ToString("ddd, d MMM yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));

        string author = null;

        if (string.IsNullOrEmpty(inboxItem.ItemType) == false)
        {
          author = "INBOX-" + inboxItem.ItemType;
        }
        if (string.IsNullOrEmpty(author))
          author = "INBOX-Unknown <null>";

        From = author;
        DisplayName = author;

        string sub = string.Empty;
        if ((inboxItem.Site != null) && (string.IsNullOrEmpty(inboxItem.Site.Name) == false))
          sub = string.Format("[{0}] ", inboxItem.Site.Name);
        Subject = sub + mapping.Title;

        Newsgroups = g.GroupName;
        ParentNewsgroup = Newsgroups;
        Path = "LOCALHOST.StackAppBridge";

        var mhStr = new StringBuilder();
        mhStr.Append("<br/>-----<br/>");
        mhStr.Append("<strong>INBOX: ");
        mhStr.Append(inboxItem.ItemType);
        mhStr.Append("</strong>");
        if (string.IsNullOrEmpty(inboxItem.Link) == false)
          mhStr.AppendFormat("<br/>Link: <a href='{0}'>{0}</a>", inboxItem.Link);
        if (inboxItem.CommentId != 0)
          mhStr.AppendFormat("<br/>CommentId: {0}", inboxItem.CommentId);
        if (inboxItem.AnswerId != 0)
          mhStr.AppendFormat("<br/>AnswerId: {0}", inboxItem.AnswerId);
        if (inboxItem.QuestionId != 0)
          mhStr.AppendFormat("<br/>QuestionId: {0}", inboxItem.QuestionId);
        if (inboxItem.CreationDate != DateTime.MinValue)
          mhStr.AppendFormat("<br/>Created: {0:yyyy-MM-dd HH:mm:ss}", inboxItem.CreationDate);
        mhStr.Append("<br/>-----<br/>");

        Body = inboxItem.Body + mhStr;
      }

      public Mapping MappingValue;


      // The "-" is a valid character in the messageId field:
      // http://www.w3.org/Protocols/rfc1036/rfc1036.html#z2
      public static string PostIdToId(string server, long postId, DateTime? createdDate)
      {
        string createdDateStr = createdDate == null
                              ? "0"
                              : createdDate.Value.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return "<" 
          + postId.ToString(System.Globalization.CultureInfo.InvariantCulture) 
          + "-"
          + server
          + "-"
          + createdDateStr
          + "@stackappnntpbridge.codeplex.com>";
      }


        public static long? IdToPostId(string id)
        {
          if (id == null) return null;
          if (id.StartsWith("<") == false) return null;
          id = id.Trim('<', '>');
          var parts = id.Split('-', '@');

          // The first part is always the id:
          long idVal;
          if (long.TryParse(parts[0], out idVal) == false)
            return null;

          return idVal;
        }


        public const int InboxTypInboxItem = 0;
        public const int InboxTypNotification = 1;
        public static string MappingToInboxId(Mapping m)
      {
        string createdDateStr = m.CreatedDate == null
                              ? "0"
                              : m.CreatedDate.Value.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return "<"
          + m.Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture)
          + "-"
          + (m.PostType == InboxTypInboxItem ? "Inbox" : "Notify")
          + "-"
          + createdDateStr
          + "@stackappnntpbridge.codeplex.com>";
      }

        public static Mapping IdToInboxMapping(string id)
        {
          if (id == null) return null;
          if (id.StartsWith("<") == false) return null;
          id = id.Trim('<', '>');
          var parts = id.Split('-', '@');

          // The first part is always the GUID:
          Guid idVal;
          if (Guid.TryParse(parts[0], out idVal) == false)
            return null;

          //DateTime? dtVal = null;
          //try
          //{
          //  dtVal = DateTime.ParseExact(parts[1], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
          //}
          //catch {}
          //if (dtVal == null)
          //  return null;

          var m = new Mapping();
          //m.CreatedDate = dtVal.Value;
          m.Id = idVal;
          return m;
        }
    }



  /// <summary>
  /// This class is responsible for providing the corret message number for a forum / tread / message
  /// </summary>
  /// <remarks>
  /// </remarks>
  internal class MsgNumberManagement
  {
    public MsgNumberManagement(string basePath, string acessToken)
    {
      _accessToken = acessToken;
      _baseDir = System.IO.Path.Combine(basePath, "Data");
      if (System.IO.Directory.Exists(_baseDir) == false)
      {
        System.IO.Directory.CreateDirectory(_baseDir);
      }

      _db = new LocalDbAccess(_baseDir);
    }

    private readonly LocalDbAccess _db;
    private readonly string _baseDir;
    private readonly string _accessToken;

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

    private string NotifyTitle(Notification n)
    {
      var sb = new StringBuilder();
      if ((n.Site != null) && (string.IsNullOrEmpty(n.Site.Name) == false))
        sb.AppendFormat("[{0}] ", n.Site.Name);
      sb.Append(n.NotificationType);
      if (n.PostId != 0)
        sb.AppendFormat(" (Post: {0})", n.PostId);
      return sb.ToString();
    }

    private IEnumerable<ForumArticle> UpdateInboxAndNotifyItems(ForumNewsgroup group, Action<string> progress,
                                                                Action<ForumArticle> articleConverter)
    {
      GetMaxMessageNumber(group);

      var internalArticles = new List<ForumArticle>();
      int maxNumber = group.LastArticle;
      using (var con = _db.CreateConnection(group.GroupName))
      {
        // InboxItems
        foreach (string site in group.InboxSites)
        {
          var inboxItems = group.StackyClient.GetInboxItems(accessToken: _accessToken,
                                                            _filter: ContentFilterNameWithBody, site_: site);
          foreach (InboxItem inboxItem in inboxItems)
          {
            DateTime cd = inboxItem.CreationDate;
            var existing =
              con.Mappings.Where(
                p => p.CreatedDate == cd && p.PostType == ForumArticle.InboxTypInboxItem)
                 .ToArray();
            if ((existing.Any() == false) ||
                (existing.Any(p => string.Equals(p.Title, inboxItem.Title, StringComparison.Ordinal)) == false))
            {
              // It does not exist.. so add it...
              var map = new Mapping();
              map.Id = Guid.NewGuid();
              map.PostType = ForumArticle.InboxTypInboxItem;
              map.NNTPMessageNumber = ++maxNumber;
              map.PostId = map.NNTPMessageNumber;
              map.CreatedDate = inboxItem.CreationDate;
              map.Title = inboxItem.Title;

              // Reset alll unused items...
              inboxItem.Title = null;
              if (inboxItem.Site != null)
              {
                inboxItem.Site.Aliases = null;
                inboxItem.Site.ApiEndpoint = null;
                inboxItem.Site.ApiSiteParameter = null;
                inboxItem.Site.Audience = null;
                inboxItem.Site.ClosedBetaDate = DateTime.MinValue;
                inboxItem.Site.Description = null;
                inboxItem.Site.FaviconUrl = null;
                inboxItem.Site.IconUrl = null;
                inboxItem.Site.LaunchDate = DateTime.MinValue;
                inboxItem.Site.LogoUrl = null;
                inboxItem.Site.MarkdownExtensions = null;
                inboxItem.Site.OpenBetaDate = DateTime.MinValue;
                inboxItem.Site.SiteUrl = null;
                inboxItem.Site.Styling = null;
                inboxItem.Site.TwitterAccount = null;
                inboxItem.Site.Type = null;
              }
              map.Info = inboxItem.ToJson();
              con.Mappings.AddObject(map);

              internalArticles.Add(new ForumArticle(group, map, inboxItem));
            }
          }
        }


        // Notification
        var notifyItems = group.StackyClient.GetNotifications(accessToken: _accessToken,
                                                              _filter: ContentFilterNameWithBody);
        foreach (Notification notifyItem in notifyItems)
        {
          DateTime cd = notifyItem.CreationDate;
          var existing =
            con.Mappings.Where(
              p => p.CreatedDate == cd && p.PostType == ForumArticle.InboxTypNotification)
               .ToArray();

          if ((existing.Any() == false) || 
            (existing.Any(p => string.Equals(p.Title, NotifyTitle(notifyItem), StringComparison.Ordinal)) == false))
          {
            // It does not exist.. so add it...
            var map = new Mapping();
            map.PostType = ForumArticle.InboxTypNotification;
            map.Id = Guid.NewGuid();
            map.NNTPMessageNumber = ++maxNumber;
            map.PostId = map.NNTPMessageNumber;
            map.CreatedDate = notifyItem.CreationDate;
            map.Title = NotifyTitle(notifyItem);

            // Reset alll unused items...
            if (notifyItem.Site != null)
            {
              notifyItem.Site.Aliases = null;
              notifyItem.Site.ApiEndpoint = null;
              notifyItem.Site.ApiSiteParameter = null;
              notifyItem.Site.Audience = null;
              notifyItem.Site.ClosedBetaDate = DateTime.MinValue;
              notifyItem.Site.Description = null;
              notifyItem.Site.FaviconUrl = null;
              notifyItem.Site.IconUrl = null;
              notifyItem.Site.LaunchDate = DateTime.MinValue;
              notifyItem.Site.LogoUrl = null;
              notifyItem.Site.MarkdownExtensions = null;
              notifyItem.Site.OpenBetaDate = DateTime.MinValue;
              notifyItem.Site.SiteUrl = null;
              notifyItem.Site.Styling = null;
              notifyItem.Site.TwitterAccount = null;
              notifyItem.Site.Type = null;
            }
            map.Info = notifyItem.ToJson();
            con.Mappings.AddObject(map);

            internalArticles.Add(new ForumArticle(group, map, notifyItem));
          }
        }

        if (internalArticles.Any())
        {
          con.SaveChanges(SaveOptions.None);
        }
        else
        {
          return null;
        }
      }

      GetMaxMessageNumber(group);
      return internalArticles;
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

        if (group.IsInboxGroup)
        {
          return UpdateInboxAndNotifyItems(group, progress, articleConverter);
        }

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
            Traces.WebService_TraceEvent(TraceEventType.Information, 1, "  Article-NotAdded: {0}", article.MappingValue.PostId);
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
                  Traces.WebService_TraceEvent(TraceEventType.Information, 1, "  Article-ParentId-Updated: id:{0}, ({1})", 
                    article.MappingValue.PostId, parentId.Value);
                }
              }
              if (parentId != null)
              {
                article.MappingValue.ParentId = parentId.Value;
                //article.UpdateParentId();
              }
              else
              {
                // Parent not found!
              }
            }
            article.Number = ++maxNr;
            article.MappingValue.NNTPMessageNumber = article.Number;

            
            Traces.WebService_TraceEvent(TraceEventType.Information, 1, "Adding to DB: id:{0} ({1}), NNTP#: {2}",
              article.MappingValue.PostId, article.MappingValue.Id, article.MappingValue.NNTPMessageNumber);

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

    void LogPageResult<T>(IPagedList<T> res, string groupName)
    {
      if (res == null)
        return;
      Traces.WebService_TraceEvent(
        TraceEventType.Information,
        1,
        "Response: CurrentPage: {0}, PageSize: {1}, HasMore: {2}, BackOff: {3}, QuotaMax: {4}, QuotaRemaining: {5} ({6})",
        res.CurrentPage, res.PageSize, res.HasMore, res.BackOff, res.QuotaMax, res.QuotaRemaining, groupName);
    }


    // Default + answer.comments;answer.link;answer.body;question.answers;question.comments;question.body;comment.link;comment.body
    internal const string QuestionFilterNameWithBody = "!0ZPuz7ZFJF)YU8rYJHAppml3Z";

    internal const string ContentFilterNameWithBody = "!LpJqjkreSIQ6*xoTTxulB3";

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

      IPagedList<Question> res = null;
      try
      {
        res = group.StackyClient.GetQuestions(QuestionSort.Activity, SortDirection.Descending, page, pageSize,
                                        filter: QuestionFilterNameWithBody,
                                        tags: group.Tags,
                                        site_: group.Site,
                                        //fromDate: lastActivityFrom);  // "fromDate" is always related to "CreationDate"!!!
                                        min_: lastActivityFrom.HasValue ?
                                        (long?)Stacky.DateHelper.ToUnixTime(lastActivityFrom.Value) : null,
                                        accessToken: _accessToken
                                        );  // And "min" is always related to "sort" value!

      }
      catch (ApiException exp)
      {
        Traces.WebService_TraceEvent(
          TraceEventType.Error, 1, "Exception of GetQuestions: Exception: {0}, Body: {1}",
          NNTPServer.Traces.ExceptionToString(exp),
          exp.Body);
        throw;
      }


      hasMore = res.HasMore;

      LogPageResult(res, group.GroupName);

      foreach (Question question in res)
      {

        // First, create the mapping-Entry:
        var map = new Mapping();
        map.PostId = question.Id;
        map.Id = Guid.NewGuid();
        map.PostType = PostTypeQuestion;
        map.Title = question.Title;
        map.CreatedDate = question.CreationDate;
        if (question.LastActivityDate != DateTime.MinValue)
          map.LastActivityDate = question.LastActivityDate;
        else
          map.LastActivityDate = question.CreationDate;

        Traces.WebService_TraceEvent(TraceEventType.Information, 1, "Question: {0} ({1})", 
          question.Id, 
          map.Id
          //question.LastActivityDate.ToString("s")
          );


        var q = new ForumArticle(group, map, question);
        result.Add(q);

        // Now also add all comments:
        if (question.Comments != null)
        {
          foreach (Comment comment in question.Comments)
          {
            // First, create the mapping-Entry:
            var mapc = new Mapping();
            mapc.PostId = comment.Id;
            mapc.Id = Guid.NewGuid();
            mapc.ParentPostId = map.PostId;
            mapc.ParentCreatedDate = map.CreatedDate;
            //mapc.ParentId = map.Id;
            mapc.PostType = PostTypeComment;
            mapc.Title = question.Title;
            mapc.CreatedDate = comment.CreationDate;

            Traces.WebService_TraceEvent(TraceEventType.Information, 1, "  Comment: qid:{0} {1} ({2})", 
              question.Id, comment.Id, mapc.Id);

            var qc = new ForumArticle(group, mapc, comment);
            result.Add(qc);
          }
        }

        // Now also add all answers
        if (question.Answers != null)
        {
          foreach (Answer answer in question.Answers)
          {
            // First, create the mapping-Entry:
            var mapa = new Mapping();
            mapa.PostId = answer.Id;
            mapa.Id = Guid.NewGuid();
            mapa.ParentPostId = map.PostId;
            mapa.ParentCreatedDate = map.CreatedDate;
            //mapa.ParentId = map.Id;
            mapa.PostType = PostTypeAnswer;
            mapa.Title = question.Title;
            mapa.CreatedDate = answer.CreationDate;

            Traces.WebService_TraceEvent(TraceEventType.Information, 1, "  Answer: qid:{0} {1} ({2})", 
              question.Id, answer.Id, 
              mapa.Id
              //answer.LastActivityDate.ToString("s")
              );

            var ac = new ForumArticle(group, mapa, answer);
            result.Add(ac);

            // Now also add all comments from this answer
            if (answer.Comments != null)
            {
              foreach (Comment comment2 in answer.Comments)
              {
                // First, create the mapping-Entry:
                var mapc2 = new Mapping();
                mapc2.PostId = comment2.Id;
                mapc2.Id = Guid.NewGuid();
                mapc2.ParentPostId = mapa.PostId;
                mapc2.ParentCreatedDate = mapa.CreatedDate;
                //mapc2.ParentId = mapa.Id;
                mapc2.PostType = PostTypeComment;
                mapc2.Title = question.Title;
                mapc2.CreatedDate = comment2.CreationDate;

                Traces.WebService_TraceEvent(TraceEventType.Information, 1, "    Comment: qid:{0} aid:{1} {2} ({3})",
                  question.Id, answer.Id, comment2.Id, mapc2.Id);

                var qac = new ForumArticle(group, mapc2, comment2);
                result.Add(qac);
              }
            }
          }
        }
      }

      return result;
    }

    public ForumArticle GetMessageById(ForumNewsgroup forumNewsgroup, long postId)
    {
      Mapping map;
      lock (forumNewsgroup)
      {
        using (var con = _db.CreateConnection(forumNewsgroup.GroupName))
        {
          map = con.Mappings.FirstOrDefault(p => p.PostId == postId);
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
      int postId = (int)map.PostId;
      switch (map.PostType)
      {
        case PostTypeQuestion:
          {
            IPagedList<Question> result = group.StackyClient.GetQuestions(new[] { postId }, _filter: ContentFilterNameWithBody, site_: group.Site, accessToken: _accessToken);
            LogPageResult(result, group.GroupName);
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
            IPagedList<Answer> result = group.StackyClient.GetAnswers(new[] { postId }, filter_: ContentFilterNameWithBody, site_: group.Site, accessToken: _accessToken);
            LogPageResult(result, group.GroupName);
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
            IPagedList<Comment> result = group.StackyClient.GetComments(new[] { postId }, filter_: ContentFilterNameWithBody, site_: group.Site, accessToken: _accessToken);
            LogPageResult(result, group.GroupName);
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
    
    public IEnumerable<ForumArticle> GetMessageStreamByMsgNo(ForumNewsgroup group, IEnumerable<int> missingArticles)
    {
      var maps = new List<Mapping>();
        using (var con = _db.CreateConnection(group.GroupName))
        {
          maps.AddRange(missingArticles.Select(articleNumber => con.Mappings.FirstOrDefault(p => p.NNTPMessageNumber == articleNumber)).Where(map => map != null));
        }
      
      // Now differentiate between Answer/Questions and Comments
        Mapping[] questions = maps.Where(p => p.PostType == PostTypeQuestion).ToArray();
      Mapping[] answers = maps.Where(p => p.PostType == PostTypeAnswer).ToArray();
      Mapping[] comments = maps.Where(p => p.PostType == PostTypeComment).ToArray();


      var res = new List<ForumArticle>();

      if (questions.Any())
      {
        IPagedList<Question> result = group.StackyClient.GetQuestions(questions.Select(p => (int)p.PostId), _filter: ContentFilterNameWithBody, site_: group.Site, accessToken: _accessToken);
        LogPageResult(result, group.GroupName);
        foreach (Question question in result)
        {
          var map = questions.First(r => r.PostId == question.Id);
          Traces.WebService_TraceEvent(TraceEventType.Information, 1, "GetQuestions: id:{0}", map.PostId);
          res.Add(new ForumArticle(group, map, question));
        }
      }

      if (answers.Any())
      {
        IPagedList<Answer> result = group.StackyClient.GetAnswers(answers.Select(p => (int)p.PostId), filter_: ContentFilterNameWithBody, site_: group.Site, accessToken: _accessToken);
        LogPageResult(result, group.GroupName);
        foreach (Answer answer in result)
        {
          var map = answers.First(r => r.PostId == answer.Id);
          Traces.WebService_TraceEvent(TraceEventType.Information, 1, "GetAnswers: id:{0}", map.PostId);
          res.Add(new ForumArticle(group, map, answer));
        }
      }

      if (comments.Any())
      {
        IPagedList<Comment> result = group.StackyClient.GetComments(comments.Select(p => (int)p.PostId), filter_: ContentFilterNameWithBody, site_: group.Site, accessToken: _accessToken);
        LogPageResult(result, group.GroupName);
        foreach (Comment comment in result)
        {
          var map = comments.First(r => r.PostId == comment.Id);
          Traces.WebService_TraceEvent(TraceEventType.Information, 1, "GetComments: id:{0}", map.PostId);
          res.Add(new ForumArticle(group, map, comment));
        }
      }

      // So, now sort the articles again (by NNTP Number)...
      res.Sort((a, b) =>
        {
          if (a.MappingValue.NNTPMessageNumber == b.MappingValue.NNTPMessageNumber)
            return 0;
          if (a.MappingValue.NNTPMessageNumber < b.MappingValue.NNTPMessageNumber)
            return -1;
          return 1;
        });

      return res;
     }

    public ForumArticle GetInboxItemFromId(ForumNewsgroup group, string articleId)
    {
      using (var con = _db.CreateConnection(group.GroupName))
      {
        Mapping map1 = ForumArticle.IdToInboxMapping(articleId);

        if (map1 != null)
        {
          Mapping map = con.Mappings.FirstOrDefault(p => p.Id == map1.Id);
          if (map != null)
          {
            if (map.PostType == ForumArticle.InboxTypInboxItem)
            {
              InboxItem inboxItem = InboxItem.FromJson(map.Info);
              return new ForumArticle(group, map, inboxItem);
            }
            if (map.PostType == ForumArticle.InboxTypNotification)
            {
              Notification notify = Notification.FromJson(map.Info);
              return new ForumArticle(group, map, notify);
            }
          }
        }
        return null;
      }
    }

    public ForumArticle GetInboxItemFromNumber(ForumNewsgroup group, int articleNumber)
    {
      using (var con = _db.CreateConnection(group.GroupName))
      {
        Mapping map = con.Mappings.FirstOrDefault(p => p.NNTPMessageNumber == articleNumber);
        if (map != null)
        {
          if (map.PostType == ForumArticle.InboxTypInboxItem)
          {
            InboxItem inboxItem = InboxItem.FromJson(map.Info);
            return new ForumArticle(group, map, inboxItem);
          }
          if (map.PostType == ForumArticle.InboxTypNotification)
          {
            Notification notify = Notification.FromJson(map.Info);
            return new ForumArticle(group, map, notify);
          }
        }
        return null;
      }
    }
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
