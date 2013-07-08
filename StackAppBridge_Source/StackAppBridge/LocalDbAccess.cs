using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace StackAppBridge
{
  public class LocalDbAccess
  {
    static LocalDbAccess()
    {
      _FileCreateAppId = Guid.NewGuid();
    }

    private const string CeConnectionString =
      @"metadata=res://*/Model1.csdl|res://*/Model1.ssdl|res://*/Model1.msl;provider=System.Data.SqlServerCe.3.5;provider connection string=""Data Source=###DATABASE###;Max Database Size=4091""";
      //@"metadata=res://*/AnswersData.csdl|res://*/AnswersData.ssdl|res://*/AnswersData.msl;provider=System.Data.SqlServerCe.3.5;provider connection string=""Data Source=###DATABASE###;Max Database Size=4091""";

    private const string CeNativeConnectionString =
      @"provider=System.Data.SqlServerCe.3.5;provider connection string=""Data Source=###DATABASE###;Max Database Size=4091""";

    private string _BasePath;

    public LocalDbAccess(string basePath)
    {
      //const int dbVersionNumber = 1;

      // First: Check if the sdf.file is available
      basePath = Environment.ExpandEnvironmentVariables(basePath);

      if (Directory.Exists(basePath) == false)
        Directory.CreateDirectory(basePath);

      _BasePath = basePath;


      //using (var dc = CreateConnection())
      //{
      //  bool wrongVersion = true;
      //  try
      //  {
      //    var v = dc.DBVersionInfoSet.FirstOrDefault(p => true);
      //    if (v != null)
      //    {
      //      wrongVersion = v.Version != dbVersionNumber;
      //    }

      //    // try to save the active groups...
      //    // TODO: 
      //  }
      //  catch
      //  {
      //  }
      //  if (wrongVersion)
      //  {
      //    // INSERT INTO [DBVersionInfoSet] ([Id], [Version]) VALUES ('{383B3BD9-BF14-4AFB-B33C-2AD1486CC144}', 1)
      //    // GO


      //    // Database already exists...
      //    // TODO: Versioning...
      //    if (
      //        MessageBox.Show("Database-Version does not match! Please reinstall the application!",
      //                        "Warning", MessageBoxButton.OK) ==
      //        MessageBoxResult.Yes)
      //    {
      //      //// ... rebuild...
      //      //dc.DeleteDatabase();
      //      //CreateDatabaseWithVersion(dc, dbVersionNumber);

      //      //// TODO: Rebuild previously active groups...
      //    }
      //  }
      //}
    }

    private static Guid _FileCreateAppId;

    private string GetSqlCeDbFileName(string groupName, bool createDatabaseIfItDoesNotExist = true)
    {
      string fn = Path.Combine(_BasePath, groupName);
      if (Directory.Exists(fn) == false)
        Directory.CreateDirectory(fn);

      fn = Path.Combine(fn, "NewsgroupData.sdf");

      if (File.Exists(fn) == false)
      {
        //string[] names = this.GetType().Assembly.GetManifestResourceNames();
        if (createDatabaseIfItDoesNotExist == false)
          return null;

        // Here I need to use a mutex to prevent duplicate creating of the file... this leads to sharing violations...
        string mutexName = _FileCreateAppId.ToString("D", System.Globalization.CultureInfo.InvariantCulture) + groupName.ToLowerInvariant();
        using (var m = new System.Threading.Mutex(false, mutexName))
        {
          m.WaitOne();
          try
          {
            if (File.Exists(fn) == false) // prüfe hier nochmals, da ich nur hier den Mutex besitze!
            {
              using (var db =
                typeof(NewsgroupsEntities).Assembly.GetManifestResourceStream(
                  "StackAppBridge.Newsgroups.sdf"))
              {
                string[] names = this.GetType().Assembly.GetManifestResourceNames();
                byte[] data = new byte[db.Length];
                db.Read(data, 0, data.Length);
                string fn2 = fn + "_tmp";
                using (var f = File.Create(fn2))
                {
                  f.Write(data, 0, data.Length);
                }
                File.Move(fn2, fn);
              }
            }
          }
          finally
          {
            m.ReleaseMutex();
          }
        }
      }
      return fn;
    }
    public System.Data.SqlServerCe.SqlCeConnection CreateSqlCeConnection(string groupName, bool createDatabaseIfItDoesNotExist = true)
    {
      string fn = GetSqlCeDbFileName(groupName, createDatabaseIfItDoesNotExist);
      if (fn == null)
        return null;

      var conStr = CeNativeConnectionString.Replace("###DATABASE###", fn);

      return new System.Data.SqlServerCe.SqlCeConnection(conStr);
    }
    public NewsgroupsEntities CreateConnection(string groupName, bool createDatabaseIfItDoesNotExist = true)
    {
      string fn = GetSqlCeDbFileName(groupName, createDatabaseIfItDoesNotExist);
      if (fn == null)
        return null;

      var conStr = CeConnectionString.Replace("###DATABASE###", fn);

      return new NewsgroupsEntities(conStr);
    }
  }
}

namespace StackAppBridge
{
  public partial class Mapping
  {

    public int ParentPostId;
    //public DateTime? LastEditDateUtc
    //{
    //  get
    //  {
    //    if (LastEditDate.HasValue == false)
    //      return null;
    //    if (LastEditDate.Value.Kind == DateTimeKind.Utc)
    //      return LastEditDate.Value;
    //    // Convert it to UTC
    //    var dt = LastEditDate.Value;
    //    return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond, DateTimeKind.Utc);
    //  }
    //}
  }
}
