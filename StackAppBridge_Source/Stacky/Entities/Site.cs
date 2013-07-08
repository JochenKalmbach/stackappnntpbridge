using Newtonsoft.Json;
using System;

namespace Stacky
{
    public class Site : Entity
    {
		private string type;
        private string name;
        private string logoUrl;
		private string apiSiteParameter;
		private string audience;
        private string siteUrl;
        private string iconUrl;
        private SiteState state;
        private string[] aliases;
        private SiteStyle styling;

		private DateTime closedBetaDate;
		private DateTime openBetaDate;
		private DateTime launchDate;
		private string faviconUrl;
		private string[] twitterAccount;
		private string[] markdownExtensions;

		//TODO: Add related_sites

		[JsonProperty("site_type")]
		public string Type
		{
			get { return type; }
			set { type = value; NotifyOfPropertyChange(() => Type); }
		}

        [JsonProperty("name")]
        public string Name
        {
            get { return name; }
            set { name = value; NotifyOfPropertyChange(() => Name); }
        }

        [JsonProperty("logo_url")]
        public string LogoUrl
        {
            get { return logoUrl; }
            set { logoUrl = value; NotifyOfPropertyChange(() => LogoUrl); }
        }

		[JsonProperty("api_site_parameter")]
        public string ApiSiteParameter
        {
            get { return apiSiteParameter; }
			set { apiSiteParameter = value; NotifyOfPropertyChange(() => ApiSiteParameter); }
        }

		[JsonProperty("audience")]
		public string Audience
		{
			get { return audience; }
			set { audience = value; NotifyOfPropertyChange(() => Audience); }
		}

        [JsonProperty("site_url")]
        public string SiteUrl
        {
            get { return siteUrl; }
            set { siteUrl = value; NotifyOfPropertyChange(() => SiteUrl); }
        }

        [JsonProperty("icon_url")]
        public string IconUrl
        {
            get { return iconUrl; }
            set { iconUrl = value; NotifyOfPropertyChange(() => IconUrl); }
        }

        [JsonProperty("state"), JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public SiteState State
        {
            get { return state; }
            set { state = value; NotifyOfPropertyChange(() => State); }
        }

        [JsonProperty("aliases")]
        public string[] Aliases
        {
            get { return aliases; }
            set { aliases = value; NotifyOfPropertyChange(() => Aliases); }
        }

        [JsonProperty("styling")]
        public SiteStyle Styling
        {
            get { return styling; }
            set { styling = value; NotifyOfPropertyChange(() => Styling); }
        }

		[JsonProperty("closed_beta_date")]
		public DateTime ClosedBetaDate
		{
			get { return closedBetaDate; }
			set { closedBetaDate = value; NotifyOfPropertyChange(() => ClosedBetaDate); }
		}

		[JsonProperty("open_beta_date")]
		public DateTime OpenBetaDate
		{
			get { return openBetaDate; }
			set { openBetaDate = value; NotifyOfPropertyChange(() => OpenBetaDate); }
		}

		[JsonProperty("launch_date")]
		public DateTime LaunchDate
		{
			get { return launchDate; }
			set { launchDate = value; NotifyOfPropertyChange(() => LaunchDate); }
		}

		[JsonProperty("favicon_url")]
		public string FaviconUrl
		{
			get { return faviconUrl; }
			set { faviconUrl = value; NotifyOfPropertyChange(() => FaviconUrl); }
		}

		[JsonProperty("twitter_account")]
		public string[] TwitterAccount
		{
			get { return twitterAccount; }
			set { twitterAccount = value; NotifyOfPropertyChange(() => TwitterAccount); }
		}

		[JsonProperty("markdown_extensions")]
		public string[] MarkdownExtensions
		{
			get { return markdownExtensions; }
			set { markdownExtensions = value; NotifyOfPropertyChange(() => MarkdownExtensions); }
		}

      [JsonIgnore]
      public string ApiEndpoint { get; set; }

      [JsonIgnore]
      public string Description { get; set; }
    }
}