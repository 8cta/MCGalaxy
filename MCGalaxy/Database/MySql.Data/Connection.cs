// Copyright � 2004, 2018, Oracle and/or its affiliates. All rights reserved.
//
// MySQL Connector/NET is licensed under the terms of the GPLv2
// <http://www.gnu.org/licenses/old-licenses/gpl-2.0.html>, like most 
// MySQL Connectors. There are special exceptions to the terms and 
// conditions of the GPLv2 as it is applied to this software, see the 
// FLOSS License Exception
// <http://www.mysql.com/about/legal/licensing/foss-exception.html>.
//
// This program is free software; you can redistribute it and/or modify 
// it under the terms of the GNU General Public License as published 
// by the Free Software Foundation; version 2 of the License.
//
// This program is distributed in the hope that it will be useful, but 
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY 
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License 
// for more details.
//
// You should have received a copy of the GNU General Public License along 
// with this program; if not, write to the Free Software Foundation, Inc., 
// 51 Franklin St, Fifth Floor, Boston, MA 02110-1301  USA

using System;
using System.Data;
using MCGalaxy.SQL;
using MySql.Data.Common;

namespace MySql.Data.MySqlClient
{
  public sealed class MySqlConnection : IDBConnection
  {
    internal ConnectionState connectionState;
    internal Driver driver;
    internal bool hasBeenOpen;
    private bool isInUse;
    private bool isKillQueryConnection;
    private string database;
    private int commandTimeout;

    /// <include file='docs/MySqlConnection.xml' path='docs/InfoMessage/*'/>
    public event MySqlInfoMessageEventHandler InfoMessage;

    private static Cache<string, MySqlConnectionStringBuilder> connectionStringCache =
      new Cache<string, MySqlConnectionStringBuilder>(0, 25);

    public MySqlConnection()
    {
      //TODO: add event data to StateChange docs
      Settings = new MySqlConnectionStringBuilder();
      database = String.Empty;
    }

    public MySqlConnection(string connectionString)
      : this()
    {
      ConnectionString = connectionString;
    }

    ~MySqlConnection() { Dispose(false); }

    #region Interal Methods & Properties

    internal MySqlConnectionStringBuilder Settings { get; private set; }

    internal MySqlDataReader Reader
    {
      get
      {
        if (driver == null)
          return null;
        return driver.reader;
      }
      set
      {
        driver.reader = value;
        isInUse = driver.reader != null;
      }
    }

    internal void OnInfoMessage(MySqlInfoMessageEventArgs args)
    {
      if (InfoMessage != null)
      {
        InfoMessage(this, args);
      }
    }

    internal bool IsInUse
    {
      get { return isInUse; }
      set { isInUse = value; }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Returns the id of the server thread this connection is executing on
    /// </summary>
    public int ServerThread
    {
      get { return driver.ThreadID; }
    }

    public int ConnectionTimeout
    {
      get { return (int)Settings.ConnectionTimeout; }
    }

    public string Database
    {
      get { return database; }
    }

    public ConnectionState State
    {
      get { return connectionState; }
    }

    public string ConnectionString
    {
      get { return Settings.ConnectionString; }
      set
      {
        if (State != ConnectionState.Closed)
          throw new MySqlException(
            "Not allowed to change the 'ConnectionString' property while the connection (state=" + State + ").");

        MySqlConnectionStringBuilder newSettings;
        lock (connectionStringCache)
        {
          if (value == null)
            newSettings = new MySqlConnectionStringBuilder();
          else
          {
            newSettings = (MySqlConnectionStringBuilder)connectionStringCache[value];
            if (null == newSettings)
            {
              newSettings = new MySqlConnectionStringBuilder(value);
              connectionStringCache.Add(value, newSettings);
            }
          }
        }

        Settings = newSettings;

        if (Settings.Database != null && Settings.Database.Length > 0)
          this.database = Settings.Database;

        if (driver != null)
          driver.Settings = newSettings;
      }
    }

    #endregion

    public IDBTransaction BeginTransaction()
    {
      //TODO: check note in help
      if (State != ConnectionState.Open)
        throw new InvalidOperationException("The connection is not open");

      // First check to see if we are in a current transaction
      if (driver.HasStatus(ServerStatusFlags.InTransaction))
        throw new InvalidOperationException("Nested transactions are not supported");

      MySqlTransaction t = new MySqlTransaction(this);
      MySqlCommand cmd = new MySqlCommand("", this);

      cmd.CommandText = "SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ";
      cmd.ExecuteNonQuery();

      cmd.CommandText = "BEGIN";
      cmd.ExecuteNonQuery();

      return t;
    }

    public void ChangeDatabase(string databaseName)
    {
      if (databaseName == null || databaseName.Trim().Length == 0)
        throw new ArgumentException("Parameter is invalid", "databaseName");

      if (State != ConnectionState.Open)
        throw new InvalidOperationException("The connection is not open");

      // This lock  prevents promotable transaction rollback to run
      // in parallel
      lock (driver)
      {
        // We use default command timeout for SetDatabase
        using (new CommandTimer(this, (int)Settings.DefaultCommandTimeout))
        {
          driver.SetDatabase(databaseName);
        }
      }
      this.database = databaseName;
    }

    internal void SetState(ConnectionState newConnectionState, bool broadcast)
    {
      if (newConnectionState == connectionState && !broadcast)
        return;
      ConnectionState oldConnectionState = connectionState;
      connectionState = newConnectionState;
    }

    public void Open()
    {
      if (State == ConnectionState.Open)
        throw new InvalidOperationException("The connection is already open");
      SetState(ConnectionState.Connecting, true);

      try
      {
        MySqlConnectionStringBuilder currentSettings = Settings;

        if (Settings.Pooling)
        {
          MySqlPool pool = MySqlPoolManager.GetPool(currentSettings);
          if (driver == null || !driver.IsOpen)
            driver = pool.GetConnection();

        }
        else
        {
          if (driver == null || !driver.IsOpen)
            driver = Driver.Create(currentSettings);
        }
      }
      catch (Exception ex)
      {
        SetState(ConnectionState.Closed, true);
        throw;
      }

      SetState(ConnectionState.Open, false);
      driver.Configure(this);

      if (!(driver.SupportsPasswordExpiration && driver.IsPasswordExpired))
      {
        if (Settings.Database != null && Settings.Database != String.Empty)
          ChangeDatabase(Settings.Database);
      }

      hasBeenOpen = true;
      SetState(ConnectionState.Open, true);
    }

    public IDBCommand CreateCommand()
    {
      // Return a new instance of a command object.
      MySqlCommand c = new MySqlCommand();
      c.Connection = this;
      return c;
    }

    internal void Abort()
    {
      try
      {
        driver.Close();
      }
      catch (Exception ex)
      {
        MySqlTrace.LogWarning(ServerThread, String.Concat("Error occurred aborting the connection. Exception was: ", ex.Message));
      }
      finally
      {
        this.isInUse = false;
      }
      SetState(ConnectionState.Closed, true);
    }

    internal void CloseFully()
    {
      if (Settings.Pooling && driver.IsOpen)
      {
        // if we are in a transaction, roll it back
        if (driver.HasStatus(ServerStatusFlags.InTransaction))
        {
          MySqlTransaction t = new MySqlTransaction(this);
          t.Rollback();
        }

        MySqlPoolManager.ReleaseConnection(driver);
      }
      else
        driver.Close();
      driver = null;
    }

    public void Close()
    {
      if (driver != null)
        driver.IsPasswordExpired = false;

      if (State == ConnectionState.Closed) return;

      if (Reader != null)
        Reader.Close();

      // if the reader was opened with CloseConnection then driver
      // will be null on the second time through
      if (driver != null) CloseFully();

      SetState(ConnectionState.Closed, true);
    }


    
    internal void HandleTimeoutOrThreadAbort(Exception ex)
    {
      bool isFatal = false;

      if (isKillQueryConnection)
      {
        // Special connection started to cancel a query.
        // Abort will prevent recursive connection spawning
        Abort();
        if (ex is TimeoutException)
        {
          throw new MySqlException("Timeout expired. The timeout period elapsed prior to completion of the operation or the server is not responding", true, ex);
        }
        else
        {
          return;
        }
      }

      try
      {

        // Do a fast cancel.The reason behind small values for connection
        // and command timeout is that we do not want user to wait longer
        // after command has already expired.
        // Microsoft's SqlClient seems to be using 5 seconds timeouts 
        // here as well.
        // Read the  error packet with "interrupted" message.
        CancelQuery(5);
        driver.ResetTimeout(5000);
        if (Reader != null)
        {
          Reader.Close();
          Reader = null;
        }
      }
      catch (Exception ex2)
      {
        MySqlTrace.LogWarning(ServerThread, "Could not kill query, " +
          " aborting connection. Exception was " + ex2.Message);
        Abort();
        isFatal = true;
      }
      if (ex is TimeoutException)
      {
        throw new MySqlException("Timeout expired. The timeout period elapsed prior to completion of the operation or the server is not responding", isFatal, ex);
      }
    }

    public void CancelQuery(int timeout)
    {
      MySqlConnectionStringBuilder cb = new MySqlConnectionStringBuilder(
        Settings.ConnectionString);
      cb.Pooling = false;
      cb.ConnectionTimeout = (uint)timeout;

      using (MySqlConnection c = new MySqlConnection(cb.ConnectionString))
      {
        c.isKillQueryConnection = true;
        c.Open();
        string commandText = "KILL QUERY " + ServerThread;
        MySqlCommand cmd = new MySqlCommand(commandText, c);
        cmd.CommandTimeout = timeout;
        cmd.ExecuteNonQuery();
      }
    }

    #region Routines for timeout support.

    // Problem description:
    // Sometimes, ExecuteReader is called recursively. This is the case if
    // command behaviors are used and we issue "set sql_select_limit" 
    // before and after command. This is also the case with prepared 
    // statements , where we set session variables. In these situations, we 
    // have to prevent  recursive ExecuteReader calls from overwriting 
    // timeouts set by the top level command.

    // To solve the problem, SetCommandTimeout() and ClearCommandTimeout() are 
    // introduced . Query timeout here is  "sticky", that is once set with 
    // SetCommandTimeout, it only be overwritten after ClearCommandTimeout 
    // (SetCommandTimeout would return false if it timeout has not been 
    // cleared).

    // The proposed usage pattern of there routines is following: 
    // When timed operations starts, issue SetCommandTimeout(). When it 
    // finishes, issue ClearCommandTimeout(), but _only_ if call to 
    // SetCommandTimeout() was successful.


    /// <summary>
    /// Sets query timeout. If timeout has been set prior and not
    /// yet cleared ClearCommandTimeout(), it has no effect.
    /// </summary>
    /// <param name="value">timeout in seconds</param>
    /// <returns>true if </returns>
    internal bool SetCommandTimeout(int value)
    {
      if (!hasBeenOpen)
        // Connection timeout is handled by driver
        return false;

      if (commandTimeout != 0)
        // someone is trying to set a timeout while command is already
        // running. It could be for example recursive call to ExecuteReader
        // Ignore the request, as only top-level (non-recursive commands)
        // can set timeouts.
        return false;

      if (driver == null)
        return false;

      commandTimeout = value;
      driver.ResetTimeout(commandTimeout * 1000);
      return true;
    }

    /// <summary>
    /// Clears query timeout, allowing next SetCommandTimeout() to succeed.
    /// </summary>
    internal void ClearCommandTimeout()
    {
      if (!hasBeenOpen)
        return;
      commandTimeout = 0;
      if (driver != null)
      {
        driver.ResetTimeout(0);
      }
    }
    #endregion

    #region Pool Routines

    /// <include file='docs/MySqlConnection.xml' path='docs/ClearPool/*'/>
    public static void ClearPool(MySqlConnection connection)
    {
      MySqlPoolManager.ClearPool(connection.Settings);
    }

    /// <include file='docs/MySqlConnection.xml' path='docs/ClearAllPools/*'/>
    public static void ClearAllPools()
    {
      MySqlPoolManager.ClearAllPools();
    }

    #endregion

    bool disposed;
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }
    
    void Dispose(bool disposing)
    {
      if (disposed) return;

      if (disposing && State == ConnectionState.Open) Close();
      disposed = true;
    }
  }

  /// <summary>
  /// Represents the method that will handle the <see cref="MySqlConnection.InfoMessage"/> event of a 
  /// <see cref="MySqlConnection"/>.
  /// </summary>
  public delegate void MySqlInfoMessageEventHandler(object sender, MySqlInfoMessageEventArgs args);

  /// <summary>
  /// Provides data for the InfoMessage event. This class cannot be inherited.
  /// </summary>
  public class MySqlInfoMessageEventArgs : EventArgs
  {
    /// <summary>
    /// 
    /// </summary>
    public MySqlError[] errors;
  }

  /// <summary>
  /// IDisposable wrapper around SetCommandTimeout and ClearCommandTimeout
  /// functionality
  /// </summary>
  internal class CommandTimer : IDisposable
  {
    bool timeoutSet;
    MySqlConnection connection;

    public CommandTimer(MySqlConnection connection, int timeout)
    {
      this.connection = connection;
      if (connection != null)
      {
        timeoutSet = connection.SetCommandTimeout(timeout);
      }
    }

    #region IDisposable Members
    public void Dispose()
    {
      if (timeoutSet)
      {
        timeoutSet = false;
        connection.ClearCommandTimeout();
        connection = null;
      }
    }
    #endregion
  }
}
