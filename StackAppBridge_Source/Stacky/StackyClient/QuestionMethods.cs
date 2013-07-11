using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Stacky
{
    public partial class StackyClient
    {
      public virtual IPagedList<Question> GetQuestions(QuestionSort sortBy = QuestionSort.Activity, SortDirection sortDirection = SortDirection.Descending, int? page = null, int? pageSize = null, DateTime? fromDate = null, DateTime? toDate = null, string tags = null, string site_ = "stackoverflow", string filter = null, long? min_ = null, string accessToken = null)
        {
            var sortArgs = sortBy.GetAttribute<SortArgsAttribute>();
            return GetQuestions("questions", sortArgs.UrlArgs, sortArgs.Sort, GetSortDirection(sortDirection), page, pageSize, fromDate, toDate, tags, site_, filter, min_, accessToken);
        }

        //public virtual IPagedList<Question> GetQuestionsByUser(int userId, QuestionsByUserSort sortBy = QuestionsByUserSort.Activity, SortDirection sortDirection = SortDirection.Descending, int? page = null, int? pageSize = null, bool includeBody = false, bool includeComments = false, bool includeAnswers = false, DateTime? fromDate = null, DateTime? toDate = null, int? min = null, int? max = null, string[] tags = null)
        //{
        //    return GetQuestions("users", new string[] { userId.ToString(), "questions" }, sortBy.ToString().ToLower(), GetSortDirection(sortDirection), page, pageSize, includeBody, includeComments, includeAnswers, fromDate, toDate, min, max, tags);
        //}

        //public virtual IPagedList<Question> GetFavoriteQuestions(int userId, FavoriteQuestionsSort sortBy = FavoriteQuestionsSort.Activity, SortDirection sortDirection = SortDirection.Descending, int? page = null, int? pageSize = null, bool includeBody = false, bool includeComments = false, bool includeAnswers = false, DateTime? fromDate = null, DateTime? toDate = null, int? min = null, int? max = null, string[] tags = null)
        //{
        //    return GetQuestions("users", new string[] { userId.ToString(), "favorites" }, sortBy.ToString().ToLower(), GetSortDirection(sortDirection), page, pageSize, includeBody, includeComments, includeAnswers, fromDate, toDate, min, max, tags);
        //}

        private IPagedList<Question> GetQuestions(string method, string[] urlArguments, string sort_, string sortDirection, int? page, int? pageSize, DateTime? fromDate, DateTime? toDate, string tags = null, string site_ = "stackoverflow", string _filter = null, long? min_ = null, string accessToken = null)
        {
          var response = MakeRequest<QuestionResponse>(method, urlArguments, new
          {
            key = apiKey,
            page = page ?? null,
            pagesize = pageSize ?? null,
            fromdate = fromDate.HasValue ? (long?)fromDate.Value.ToUnixTime() : null,
            todate = toDate.HasValue ? (long?)toDate.Value.ToUnixTime() : null,
            //tagged = tags == null ? (string)null : String.Join(";", tags),
            tagged = tags ?? null,
            sort = sort_,
            order = sortDirection,
            site = site_,
            filter = _filter,
            min = min_,
            access_token = accessToken,
          });
          return new PagedList<Question>(response.Questions, response);
        }

        public virtual IPagedList<Question> GetQuestions(IEnumerable<int> questionIds, int? page = null, int? pageSize = null, string site_ = "stackoverflow", string _filter = null, string accessToken = null)
        {
          var response = MakeRequest<QuestionResponse>("questions", new string[] { questionIds.Vectorize() }, new
          {
            key = apiKey,
            page = page ?? null,
            pagesize = pageSize ?? null,
            site = site_,
            filter = _filter,
            access_token = accessToken,
          });
          return new PagedList<Question>(response.Questions, response);
        }

        //public virtual Question GetQuestion(int questionId, bool includeBody = false, bool includeComments = false, bool includeAnswers = false)
        //{
        //    return GetQuestions(questionId.ToArray(), includeBody: includeBody, includeComments: includeComments, includeAnswers: includeAnswers).FirstOrDefault();
        //}

        //public virtual IPagedList<PostEvent> GetQuestionTimeline(IEnumerable<int> questionIds, int? page = null, int? pageSize = null, DateTime? fromDate = null, DateTime? toDate = null, string site_ = "stackoverflow")
        //{
        //    var response = MakeRequest<QuestionTimelineResponse>("questions", new string[] { questionIds.Vectorize(), "timeline" }, new
        //    {
        //        key = apiKey,
        //        fromdate = fromDate.HasValue ? (long?)fromDate.Value.ToUnixTime() : null,
        //        todate = toDate.HasValue ? (long?)toDate.Value.ToUnixTime() : null,
        //        page = page ?? null,
        //        pagesize = pageSize ?? null,
        //        site = site_,
        //    });
        //    return new PagedList<PostEvent>(response.Events, response);
        //}

        //public virtual IPagedList<PostEvent> GetQuestionTimeline(int questionId, int? page = null, int? pageSize = null, DateTime? fromDate = null, DateTime? toDate = null)
        //{
        //    return GetQuestionTimeline(questionId.ToArray(), page, pageSize, fromDate, toDate);
        //}

        //public virtual IPagedList<Question> Search(string inTitle = null, IEnumerable<string> tagged = null, IEnumerable<string> notTagged = null, SearchSort sortBy = SearchSort.Activity, SortDirection sortDirection = SortDirection.Descending, int? page = null, int? pageSize = null, string site_ = "stackoverflow")
        //{
        //    string taggedString = null;
        //    if (tagged != null)
        //        taggedString = String.Join(";", tagged);

        //    string notTaggedString = null;
        //    if (notTagged != null)
        //        notTaggedString = String.Join(";", notTagged);

        //    var response = MakeRequest<QuestionResponse>("search", null, new
        //    {
        //        key = apiKey,
        //        intitle = inTitle,
        //        tagged = taggedString,
        //        nottagged = notTaggedString,
        //        sort = sortBy,
        //        order = GetSortDirection(sortDirection),
        //        page = page ?? null,
        //        pagesize = pageSize ?? null,
        //        site = site_,
        //    });
        //    return new PagedList<Question>(response.Questions, response);
        //}

        //public virtual IPagedList<Question> GetLinkedQuestions(int questionId, QuestionSort sortBy = QuestionSort.Activity, SortDirection sortDirection = SortDirection.Descending, int? page = null, int? pageSize = null, bool includeBody = false, bool includeComments = false, bool includeAnswers = false, DateTime? fromDate = null, DateTime? toDate = null, int? min = null, int? max = null)
        //{
        //    return GetLinkedQuestions(questionId.ToArray(), sortBy, sortDirection, page, pageSize, includeBody, includeComments, includeAnswers, fromDate, toDate, min, max);
        //}

        //public virtual IPagedList<Question> GetLinkedQuestions(IEnumerable<int> questionIds, QuestionSort sortBy = QuestionSort.Activity, SortDirection sortDirection = SortDirection.Descending, int? page = null, int? pageSize = null, bool includeBody = false, bool includeComments = false, bool includeAnswers = false, DateTime? fromDate = null, DateTime? toDate = null, int? min = null, int? max = null)
        //{
        //    return GetQuestions("questions", new string[] { questionIds.Vectorize(), "linked" }, sortBy.ToString(), GetSortDirection(sortDirection), page, pageSize, includeBody, includeComments, includeAnswers, fromDate, toDate, min, max, null);
        //}

        //public virtual IPagedList<Question> GetRelatedQuestions(int questionId, QuestionSort sortBy = QuestionSort.Activity, SortDirection sortDirection = SortDirection.Descending, int? page = null, int? pageSize = null, bool includeBody = false, bool includeComments = false, bool includeAnswers = false, DateTime? fromDate = null, DateTime? toDate = null, int? min = null, int? max = null)
        //{
        //    return GetRelatedQuestions(questionId.ToArray(), sortBy, sortDirection, page, pageSize, includeBody, includeComments, includeAnswers, fromDate, toDate, min, max);
        //}

        //public virtual IPagedList<Question> GetRelatedQuestions(IEnumerable<int> questionIds, QuestionSort sortBy = QuestionSort.Activity, SortDirection sortDirection = SortDirection.Descending, int? page = null, int? pageSize = null, bool includeBody = false, bool includeComments = false, bool includeAnswers = false, DateTime? fromDate = null, DateTime? toDate = null, int? min = null, int? max = null)
        //{
        //    return GetQuestions("questions", new string[] { questionIds.Vectorize(), "related" }, sortBy.ToString(), GetSortDirection(sortDirection), page, pageSize, includeBody, includeComments, includeAnswers, fromDate, toDate, min, max, null);
        //}

        //public virtual IPagedList<Question> GetNoAnswerQuestions(QuestionSort sortBy = QuestionSort.Activity, SortDirection sortDirection = SortDirection.Descending, int? page = null, int? pageSize = null, bool includeBody = false, bool includeComments = false, bool includeAnswers = false, DateTime? fromDate = null, DateTime? toDate = null, int? min = null, int? max = null)
        //{
        //    return GetQuestions("questions", new string[] { "no-answers" }, sortBy.ToString(), GetSortDirection(sortDirection), page, pageSize, includeBody, includeComments, includeAnswers, fromDate, toDate, min, max, null);
        //}

        //public virtual IEnumerable<Question> GetSimilarQuestions(string title, bool includeBody = false, bool includeComments = false, bool includeAnswers = false, IEnumerable<string> tagged = null, IEnumerable<string> notTagged = null, string site_ = "stackoverflow")
        //{
        //    string taggedString = null;
        //    if (tagged != null)
        //        taggedString = String.Join(";", tagged);

        //    string notTaggedString = null;
        //    if (notTagged != null)
        //        notTaggedString = String.Join(";", notTagged);

        //    return MakeRequest<QuestionResponse>("similar", null, new
        //    {
        //        key = apiKey,
        //        title = title,
        //        body = includeBody ? (bool?)true : null,
        //        comments = includeComments ? (bool?)true : null,
        //        answers = includeAnswers ? (bool?)true : null,
        //        tagged = taggedString,
        //        nottagged = notTaggedString,
        //        site = site_,
        //    }).Questions;
        //}
    }
}