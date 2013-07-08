using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Stacky
{
    public static class Sites
    {
        public static Site StackOverflow
        {
            get
            {
                return new Site
                {
                    Name = "Stack Overflow",
                    LogoUrl = "http://sstatic.net/stackoverflow/img/logo.png",
                    ApiEndpoint = "http://api.stackexchange.com",
                    SiteUrl = "http://stackoverflow.com",
                    Description = "Q&A for professional and enthusiast programmers",
                    IconUrl = "http://sstatic.net/stackoverflow/img/apple-touch-icon.png",
                    State = SiteState.Normal
                };
            }
        }
    }
}