using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace StackAppBridge
{
  /// <summary>
  /// Interaction logic for AuthWindow.xaml
  /// </summary>
  /// <remarks>
  /// The authentication works as follows:
  /// 1. Navigate to https://stackexchange.com/oauth/dialog?client_id=1736&scope=no_expiry&redirect_uri=https://stackexchange.com/oauth/login_success
  /// 2. Wait for navigation to "https://stackexchange.com/oauth/login_success"
  /// 3. The access_token is then in the URI, like: https://stackexchange.com/oauth/login_success#access_token=abcdef
  /// See also: 
  ///   http://api.stackexchange.com/docs/authentication
  ///   http://stackapps.com/questions/3829/getting-unauthorized-error-when-using-oauth2-0
  /// </remarks>
  public partial class AuthWindow : Window
  {
    private const string baseUrl = "https://stackexchange.com/oauth/dialog?client_id=1736&scope={0}&redirect_uri=https://stackexchange.com/oauth/login_success";
    public AuthWindow(IEnumerable<string> scopes)
    {
      _url = string.Format(baseUrl, string.Join(",", scopes));
      InitializeComponent();
    }

    private string _url;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      WebBrowser.Navigate(_url);
    }

    private void WebBrowser_OnNavigated(object sender, NavigationEventArgs e)
    {
      string url = e.Uri.AbsoluteUri;
      if ( (string.IsNullOrEmpty(url) == false) && url.StartsWith("https://stackexchange.com/oauth/login_success"))
      {
        const string authTokenStr = "access_token=";
        int pos = url.IndexOf(authTokenStr);
        if (pos > 0)
        {
          pos += authTokenStr.Length;
          AccessToken = url.Substring(pos);
          this.DialogResult = true;
          this.Close();
          return;
        }

        const string errDescStr = "error_description=";
        pos = url.IndexOf(errDescStr);
        if (pos > 0)
        {
          pos += errDescStr.Length;
          ErrorDescription = url.Substring(pos);
          this.DialogResult = false;
          //this.Close();  // do not automatically close the dialog!
          return;
        }
      }
    }

    public string AccessToken { get; set; }

    public string ErrorDescription { get; set; }
  }
}
