﻿#region Using Directives

using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

#endregion

namespace Stacky
{
    /// <summary>
    /// Stack Overflow API url helper methods.
    /// </summary>
    public static class UrlHelper
    {
        /// <summary>
        /// Builds the Stack Overflow API method <see cref="System.Uri"/>.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <param name="version">The API version.</param>
        /// <param name="serviceUrl">The service URL.</param>
        /// <param name="urlParameters">The URL parameters.</param>
        /// <param name="queryStringParameters">The query string parameters.</param>
        /// <returns>Stack Overflow method call <see cref="System.Uri"/></returns>
        public static Uri BuildUrl(string method, string version,string serviceUrl, string[] urlParameters, object queryStringParameters)
        {
            return BuildUrl(method, version, serviceUrl, urlParameters, BuildParameters(queryStringParameters));
        }

        /// <summary>
        /// Builds the Stack Overflow API method <see cref="System.Uri"/>.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <param name="version">The API version.</param>
        /// <param name="serviceUrl">The service URL.</param>
        /// <param name="urlParameters">The URL parameters.</param>
        /// <param name="queryStringParameters">The query string parameters.</param>
        /// <returns>Stack Overflow method call <see cref="System.Uri"/></returns>
        public static Uri BuildUrl(string method, string version, string serviceUrl, string[] urlParameters, Dictionary<string, string> queryStringParameters)
        {
            return BuildUrl(method, version, serviceUrl, urlParameters, BuildParameters(queryStringParameters));
        }

        private static Uri BuildUrl(string method, string version, string serviceUrl, string[] urlParameters, string queryString)
        {
            Require.NotNullOrEmpty(method, "method");
            Require.NotNullOrEmpty(serviceUrl, "serviceUrl");

            string urlBase = String.Format(CultureInfo.CurrentCulture, "{0}/{1}/{2}/", serviceUrl, version, method);
            if (urlParameters != null)
            {
                foreach (string param in urlParameters)
                {
                    urlBase += String.Format("{0}/", Uri.EscapeDataString(param));
                }
            }
            Uri url = new Uri(urlBase);

            if (!String.IsNullOrEmpty(queryString))
            {
                url = new Uri(url, "?" + queryString);
            }
            return url;
        }

        /// <summary>
        /// Builds query string parameter from the properties in the provided dictionary.
        /// </summary>
        /// <param name="parameters">The property dictionary.</param>
        /// <returns>"[property0]=[value0]&[property1]=[value1]..."</returns>
        public static string BuildParameters(Dictionary<string, string> parameters)
        {
            if (parameters == null)
                return String.Empty;

            StringBuilder s = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                s.AppendFormat("{0}={1}&", Uri.EscapeDataString(pair.Key), Uri.EscapeDataString(pair.Value));
            }
            if (s.Length > 0)
                s.Remove(s.Length - 1, 1);
            return s.ToString();
        }

        /// <summary>
        /// Builds query string parameter from the properties in the provided object.
        /// </summary>
        /// <param name="parameters">The object.</param>
        /// <returns>"[property0]=[value0]&[property1]=[value1]..."</returns>
        public static string BuildParameters(object parameters)
        {
            if (parameters == null)
                return String.Empty;

            return BuildParameters(ObjectToDictionary(parameters));
        }

        /// <summary>
        /// Returns a property dictionary for the provided object.
        /// </summary>
        /// <param name="item">The object.</param>
        /// <returns>Dictionary of propery names and values.</returns>
        public static Dictionary<string, string> ObjectToDictionary(object item)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            if (item == null)
                return values;

            foreach (PropertyInfo property in item.GetType().GetProperties())
            {
                if (property.CanRead)
                {
                    object o = property.GetValue(item, null);
                    if (o != null)
                    {
                        values.Add(property.Name, o.ToString());
                    }
                }
            }
            return values;
        }

        /// <summary>
        /// Converts a list of items to a delimited string to be used
        /// by vectorized (multi-key) requests
        /// </summary>
        public static string Vectorize<T>(this IEnumerable<T> items)
        {
            return String.Join(";", items.Select(i => i.ToString()).ToArray());
        }

        /// <summary>
        /// Converts an int to int array.
        /// </summary>
        /// <param name="item">The int.</param>
        /// <returns>The int[].</returns>
        public static int[] ToArray(this int item)
        {
            return new int[] { item };
        }
    }
}