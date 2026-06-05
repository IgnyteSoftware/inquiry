//
// Class	:	DatabaseHelper.cs
// Author	:  	Inquiry ©  2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:11 PM
//
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.OleDb;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using ConfigurationManager = System.Configuration.ConfigurationManager;


namespace Inquiry.Benchmarks.DLG
{

    #region Enumerations
    
    public enum SupportedDatabases
    {
        SqlServer,
        OleDb,
        Oracle,
        ODBC,
        SQLite,
        Postgres
    }
    
    public enum ConnectionState
    {
        KeepOpen, CloseOnExit
    }
    
    #endregion
    
    public sealed class DatabaseHelper : IDisposable
    {
        [System.Runtime.InteropServices.DllImport("Rpcrt4.dll")]
        private static extern int UuidCreateSequential(ref Guid guid);
    
        #region Class Level Variables
    
        private bool _isDisposed = false;
    
        private readonly ConnectionState _connectionState = ConnectionState.CloseOnExit;
    
        private static bool _shouldLogSQLToCompactFile = false;
        private static bool _shouldLogSQLToDetailFile = false;
        private static readonly SingletonLogWriter _singletonLogWriter = SingletonLogWriter.Instance;
    
        #endregion
    
        #region Constants
    
        private const string PROVIDER_SQL_SERVER_CLIENT = "Microsoft.Data.SqlClient";
        private const string PROVIDER_SQL_SERVER_CLIENT_OLD = "System.Data.SqlClient";
        private const string PROVIDER_ORACLE_CLIENT = "Oracle.ManagedDataAccess.Core";
        private const string PROVIDER_OLEDB_CLIENT = "System.Data.OleDb";
        private const string PROVIDER_ODBC_CLIENT = "System.Data.Odbc";
        private const string PROVIDER_SQLITE_CLIENT = "Microsoft.Data.Sqlite.Core";
        private const string PROVIDER_POSTGRES_CLIENT = "Npgsql";
        private const string ERROR_LOG_FILE_NAME = "DLG DataAccess Error Log.txt";
        private const string FAILED_BACKUP_DB_SQL_STATEMENTS_LOG_FILE = "DLG Failed Backup SQL.sql";
    
        #endregion
    
        #region Constructors / Destructors
    
        /// <summary>
        /// This constructor allows an instance of the data access library to be created and cached and 
        /// it uses the same database provider and connection of an existing DatabaseHelper object.
        /// When passed in DatabaseHelper is null, it determines the type of database to instantiate 
        /// by looking at the configuration section of the config file.
        /// </summary>
        ///
        /// <param name="databaseHelper" type = "DatabaseHelper"></param>Indicates the DatabaseHelper object to use its database provider and connection
        /// 
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        public DatabaseHelper(DatabaseHelper? databaseHelper = null)
        {
            try
            {
                if (databaseHelper is null)
                {
    				var configurationInfo = ConfigurationHelper.GetInfo();
    				Initialize(configurationInfo.ConnectionString, configurationInfo.BackupConnectionString, configurationInfo.ShouldUseBackupServer, GetSupportedDatabaseFromProviderName(configurationInfo.ProviderName));
    			}
                else
                {
    				this._connectionState = ConnectionState.KeepOpen;
    				Initialize(databaseHelper);
    			}
                
            }
            catch (Exception ex)
            {
                ProcessException(ex);
            }
        }
        
        /// <summary>
				/// This constructor allows an instance of the data access library to be created and cached
				/// assuming a valid connection string is provided along with the enumeration indicating 
				/// which database provider to utilize.  In the case of a standard application configuration 
				/// file not being utilized, this would be the constructor to use.
				/// </summary>
				///
				/// <param name="connectionString" type = "string">The fully compliant .NET connection string</param>
				/// <param name="backupConnectionString" type = "string">The fully compliant .NET connection string</param>
				/// <param name="databaseToUse" type="SupportedDatabases">Indicates the type of database to connect with</param>
				///
				///	<remarks>
				///	
				/// <RevisionHistory>        
				/// Author				Date			                    Description
				/// DLGenerator			8/15/2025 2:57:18 AM              Created function
				/// 
				/// </RevisionHistory>
				/// 
				/// </remarks>
				public DatabaseHelper(string connectionString, string? backupConnectionString = null, SupportedDatabases databaseToUse = SupportedDatabases.SqlServer)
				{
				    Initialize(connectionString, backupConnectionString, false, databaseToUse);
				}
    
    
    	/// <summary>
    	/// This constructor allows an instance of the data access library to be created and cached with a connection string and database provider.
    	/// It determines the type of database to instantiate by looking at the configuration section of the config file.
    	/// </summary>
    	///
    	/// <param name="configFileAndPath" type = "string">The path and file name containing the connection string definition to be used.</param>
    	/// <param name="configFileConnectionStringKeyName" type = "string">The key name in the config file that defines the connection string to be used.</param>
    	/// 
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM			Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public DatabaseHelper(string configFileAndPath)
        {
            try
            {
                var configurationInfo = ConfigurationHelper.GetInfo(configFileAndPath);
                Initialize(configurationInfo.ConnectionString, configurationInfo.BackupConnectionString, configurationInfo.ShouldUseBackupServer, GetSupportedDatabaseFromProviderName(configurationInfo.ProviderName));
            }
            catch (Exception ex)
            {
                ProcessException(ex);
            }
    
        }
    
        public void Dispose()
        {
            // This object will be cleaned up by the Dispose method.
            // Therefore, you should call GC.SuppressFinalize to
            // take this object off the finalization queue 
            // and prevent finalization code for this object
            // from executing a second time.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    
        // Executes in two distinct scenarios:
        // If isDisposed equals true, the method has been called directly
        // or indirectly by user's code. Managed and unmanaged resources
        // can be disposed.
        // If disposing equals false, the method has been called by the 
        // runtime from inside the finalizer and you should not reference 
        // other objects. Only unmanaged resources can be disposed.
        private void Dispose(bool isDisposing)
        {
            // Check to see if Dispose has already been called.
            if (!_isDisposed)
            {
                // If isDisposing equals true cleanup all managed and unmanaged resources.
                if (isDisposing)
                {
                    // Dispose managed resources.
                    if (this._connectionState is ConnectionState.CloseOnExit)
                    {
                        CurrentConnection?.Close();
                        CurrentConnection?.Dispose();
                        CurrentConnection = null;
                    }
    
                    CurrentCommand?.Dispose();
                    CurrentCommand = null;
                    _singletonLogWriter.Flush();
                }
    
                // Call the appropriate methods to clean up 
                // unmanaged resources here.
                // If isDisposing is false, 
                // only the following code is executed.
    
            }
            _isDisposed = true;
        }
    
        #endregion
    
        #region Properties
    
		public SupportedDatabases CurrentDatabase { get; private set; } = SupportedDatabases.SqlServer;
        public int CommandTimeOut { get; set; } = 5000;
        public bool ShouldLogErrors { get; set; } = true;
        private bool _shouldUseBackupServer = false;
        public bool ShouldUseBackupServer
        {
            get { return _shouldUseBackupServer; }

						set
						{
						    if (value && (BackupConnectionString is null || BackupConnectionString.Length == 0))
						        throw new ArgumentException("A valid backup connection string must be provided before setting this property to true.");
						    _shouldUseBackupServer = value;
						}
        }
    	public static bool ShouldLogSQLToCompactFile
    	{
    		get
    		{
    			// We need to check to see if a configuration key exists in the 
    			// config file as this will override the setting of the property
    			// by the programmer.
    
    			return ConfigurationManager.AppSettings["ShouldLogSQLToCompactFile"] is not null ? ConfigurationManager.AppSettings["ShouldLogSQLToCompactFile"]!.ToUpper().Equals("TRUE") : _shouldLogSQLToCompactFile;
    		}
    		set
    		{
    			_shouldLogSQLToCompactFile = value;
    		}
    	}
    	public static string SQLLogCompactFileName
    	{
    		get
    		{
    			return SQLLoggingHelper.SQL_Compact_Log_FileName;
    		}
    		set
    		{
    			SQLLoggingHelper.SQL_Compact_Log_FileName = value;
    		}
    	}
    	public static bool ShouldLogSQLToDetailFile
    	{
    		get
    		{
    			// We need to check to see if a configuration key exists in the 
    			// config file as this will override the setting of the property
    			// by the programmer.
    
    			return ConfigurationManager.AppSettings["ShouldLogSQLToDetailFile"] is not null ? ConfigurationManager.AppSettings["ShouldLogSQLToDetailFile"]!.ToUpper().Equals("TRUE") : _shouldLogSQLToDetailFile;
    		}
    		set
    		{
    			_shouldLogSQLToDetailFile = value;
    		}
    	}
    	public string SQLLogDetailFileName
    	{
    		get
    		{
    			return SQLLoggingHelper.SQL_Detail_Log_FileName;
    		}
    		set
    		{
    			SQLLoggingHelper.SQL_Detail_Log_FileName = value;
    		}
    	}
    
    	public DbProviderFactory? ProviderFactory { get; internal set; }
			//Primary Server
			public string ConnectionString { get; set; } = string.Empty;
      public DbConnection? CurrentConnection { get; internal set; }
      public DbTransaction? CurrentTransaction { get; internal set; }
      public DbCommand? CurrentCommand { get; internal set; }
      public DbDataReader? CurrentDataReader { get; internal set; }
      public string? UserIDFromConnectionString => ConnectionString is not null ? GetUserIdFromConnectionString(ConnectionString) : null;
			public string? ServerFromConnectionString => ConnectionString is not null ? GetServerFromConnectionString(ConnectionString) : null;
    
			public string LastAttemptedSQL { get; private set; } = string.Empty;
    	public string LastExecutedSQL { get; private set; } = string.Empty;
    	public TimeSpan LastExecutedSQLTime { get; private set; } = TimeSpan.Zero;
			public bool ShouldSaveSQLResponseTimes { get; set; } = true;
    
      //Backup Server
			public string? BackupConnectionString { get; private set; } = null;
    
    	public DbConnection? CurrentBackupConnection { get; internal set; }
      public DbTransaction? CurrentBackupTransaction { get; internal set; }
      public DbCommand? CurrentBackupCommand { get; internal set; }
      public DbDataReader? CurrentBackupDataReader { get; internal set; }
      public string? ServerFromBackupConnectionString => BackupConnectionString is not null ? GetServerFromConnectionString(BackupConnectionString) : null;
			public string? UserIDFromBackupConnectionString => BackupConnectionString is not null ? GetUserIdFromConnectionString(BackupConnectionString) : null;
			public string LastAttemptedBackupSQL { get; private set; } = string.Empty;
    
    
    	public string LastExecutedBackupSQL { get; private set; } = string.Empty;
      //public TimeSpan LastExecutedBackupSQLTime { get; private set; } = TimeSpan.Zero;
      
			/// <summary>
			/// Retrieves all SQL statements from the failed backup log file, excluding metadata comments.
			/// </summary>
			public static string[] FailedBackupSQLStatements
			{
			    get
			    {
			
			        if (!System.IO.File.Exists(FAILED_BACKUP_DB_SQL_STATEMENTS_LOG_FILE))
			        {
			            return Array.Empty<string>();
			        }
			
			        try
			        {
			            // Read all lines from the recovery file
			            string[] allLines = System.IO.File.ReadAllLines(FAILED_BACKUP_DB_SQL_STATEMENTS_LOG_FILE);
			
			            // Filter out lines that are empty or start with the SQL comment prefix '--'
			            var sqlLines = allLines
			                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("--"))
			                .Select(line => line.Trim());
			
			            return [.. sqlLines];
			        }
			        catch (Exception)
			        {
			            // If the file is locked or unreadable, return an empty array
			            return [];
			        }
			    }
			}
    
    
    	#endregion
    
    	#region Methods (Private)
    
    	/// <summary>
    	/// Initializes this DatabaseHelper using another DatabaseHelper.
    	/// </summary>
    	///
    	/// <param name="databaseHelper" type = "DatabaseHelper">The source databaseHelper to use for initialization.</param>
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	private void Initialize(DatabaseHelper databaseHelper)
        {
            try
            {
								CurrentDatabase = databaseHelper.CurrentDatabase;
								ProviderFactory = databaseHelper.ProviderFactory;
                
                //Primary Server Setup
    						ConnectionString = databaseHelper.ConnectionString;
                CurrentConnection = databaseHelper.CurrentConnection;
                CurrentTransaction = databaseHelper.CurrentTransaction;
                CurrentCommand = ProviderFactory?.CreateCommand();
    		
                if (CurrentCommand is not null)
                {
    								CurrentCommand.Connection = CurrentConnection;
    								CurrentCommand.Transaction = CurrentTransaction;
    						}
                
                //Backup Server Setup
                BackupConnectionString = databaseHelper.BackupConnectionString;
                if (BackupConnectionString is not null && BackupConnectionString.Length > 0)
                {
										CurrentBackupConnection = databaseHelper.CurrentBackupConnection;
										CurrentBackupTransaction = databaseHelper.CurrentBackupTransaction;
										CurrentBackupCommand = ProviderFactory?.CreateCommand();
    
										if (CurrentBackupCommand is not null)
										{
										    CurrentBackupCommand.Connection = CurrentBackupConnection;
										    CurrentBackupCommand.Transaction = CurrentBackupTransaction;
										}
										_shouldUseBackupServer = databaseHelper.ShouldUseBackupServer;
                }
                else
								{
								    //Unable to setup backup server when no connection string is provided.
								    _shouldUseBackupServer = false;
								}
            }
            catch (Exception ex)
            {
                ProcessException(ex);
            }
        }
    
    	/// <summary>
    	/// Initializes this DatabaseHelper for the provided database.
    	/// </summary>
    	///
    	/// <param name="connectionString" type = "string">The database connection string.</param>
      /// <param name="backupConnectionString" type = "string?">The database connection string for the backup server.</param>
			/// <param name="shouldUseBackupServer" type = "bool">Allow DatabaseHelper to write to backup server.</param>
			/// <param name="databaseToUse" type = "SupportedDatabases">The database provider being used.</param>
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	private void Initialize(string connectionString, string? backupConnectionString, bool shouldUseBackupServer, SupportedDatabases databaseToUse = SupportedDatabases.SqlServer)
        {
            try
            {
								CurrentDatabase = databaseToUse;
                
                //Primary Server Setup
                ConnectionString = connectionString;
                var factory = GetFactory(GetFactoryProviderName(CurrentDatabase));
								ProviderFactory = factory;
    
    						CurrentConnection = factory.CreateConnection();
    						if (CurrentConnection is not null)
								{
								    CurrentConnection.ConnectionString = ConnectionString;
								}
    						CurrentCommand = factory.CreateCommand();
                if (CurrentCommand is not null)
                {
                    CurrentCommand.Connection = CurrentConnection;
                    CurrentCommand.Transaction = CurrentTransaction;
    						}
    
                //Backup Server Setup
                BackupConnectionString = backupConnectionString;
                if (BackupConnectionString is not null && BackupConnectionString.Length > 0)
                {
										CurrentBackupConnection = factory.CreateConnection();
										if (CurrentBackupConnection is not null)
										{
										    CurrentBackupConnection.ConnectionString = BackupConnectionString;
										}
										CurrentBackupCommand = factory.CreateCommand();
										if (CurrentBackupCommand is not null)
										{
										    CurrentBackupCommand.Connection = CurrentBackupConnection;
										    CurrentBackupCommand.Transaction = CurrentBackupTransaction;
										}
                _shouldUseBackupServer = shouldUseBackupServer;
                }
                else
								{
								    //Unable to setup backup server when no connection string is provided.
								    _shouldUseBackupServer = false;
								}
    
            }
            catch (Exception ex)
            {
                ProcessException(ex);
            }
        }
    
        /// <summary>
        /// This method will return a factory instance of a specific database provider based on 
        /// the connection string defined in the ConnectionStrings section of the configuration file.
        /// </summary>
        ///
        /// <param name="providerName" type="string">The name of the provider from the connection string entry.</param>
        ///
        /// <returns>An instance of the specific factory object requested.</returns>
        /// 
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        private static DbProviderFactory GetFactory(string providerName)
        {
            DbProviderFactory defaultFactory = SqlClientFactory.Instance;
    
            switch (providerName)
            {
                case PROVIDER_SQL_SERVER_CLIENT:
                case PROVIDER_SQL_SERVER_CLIENT_OLD:
                    return SqlClientFactory.Instance;
    
                case PROVIDER_ODBC_CLIENT:
                    return OdbcFactory.Instance;
            }
    
    		return defaultFactory;
    	}
    
    
        /// <summary>
        /// This method will determine the name of the identified enumerated database.
        /// </summary>
        ///
        /// <param name="databaseToUse" type="SupportedDatabases">The enumeration of the database in which to find it's name.</param>
        ///
        /// <returns>The name of the database factory provider.</returns>
        /// 
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        private static string GetFactoryProviderName(SupportedDatabases databaseToUse)
        {
            var newFactoryName = string.Empty;
            switch (databaseToUse)
            {
                case SupportedDatabases.SqlServer:
                    newFactoryName = PROVIDER_SQL_SERVER_CLIENT;
                    break;
    
                case SupportedDatabases.OleDb:
                    newFactoryName = PROVIDER_OLEDB_CLIENT;
                    break;
    
                case SupportedDatabases.Oracle:
                    newFactoryName = PROVIDER_ORACLE_CLIENT;
                    break;
    
                case SupportedDatabases.ODBC:
                    newFactoryName = PROVIDER_ODBC_CLIENT;
                    break;
    
                case SupportedDatabases.SQLite:
                    newFactoryName = PROVIDER_SQLITE_CLIENT;
                    break;
    
                case SupportedDatabases.Postgres:
                    newFactoryName = PROVIDER_POSTGRES_CLIENT;
                    break;
    				}
						return newFactoryName;
        }
        
        private static  SupportedDatabases GetSupportedDatabaseFromProviderName(string providerName)
				{
				    switch (providerName)
				    {
				        case PROVIDER_SQL_SERVER_CLIENT:
				            return SupportedDatabases.SqlServer;
				        case PROVIDER_OLEDB_CLIENT:
				            return SupportedDatabases.OleDb;
				        case PROVIDER_ORACLE_CLIENT:
				            return SupportedDatabases.Oracle;
				        case PROVIDER_ODBC_CLIENT:
				            return SupportedDatabases.ODBC;
				        case PROVIDER_SQLITE_CLIENT:
				            return SupportedDatabases.SQLite;
				        case PROVIDER_POSTGRES_CLIENT:
				            return SupportedDatabases.Postgres;
				        default:
				            throw new Exception($"Unknown provider {providerName}");
				    }
				}
    
        /// <summary>
        /// This method writes the given text to a text file.
        /// </summary>
        ///
        /// <param name="text" type="string">The information to write to the file.</param>
        /// 
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        private void WriteToLog(string text)
        {
            _singletonLogWriter.WriteText(DateTime.Now.ToString("yyyy/MM/dd hh:mm") + " - " + text, ERROR_LOG_FILE_NAME);
        }
    
        private void LogSQLStatementsIfNecessary(DbCommand commandToLog)
        {
            // determine if the application has been configured to record
            // sql statements to a file.
    
            if (ShouldLogSQLToCompactFile && ShouldLogSQLToDetailFile)
            {
                SQLLoggingHelper.LogSQLStatement(commandToLog, SQLLoggingHelper.LoggingStyle.Both);
            }
            else if (ShouldLogSQLToCompactFile)
            {
                SQLLoggingHelper.LogSQLStatement(commandToLog, SQLLoggingHelper.LoggingStyle.Compact);
            }
            else if (ShouldLogSQLToDetailFile)
            {
                SQLLoggingHelper.LogSQLStatement(commandToLog, SQLLoggingHelper.LoggingStyle.Detailed);
            }
        }
    
    	#endregion
    
    	// ---------------------------
    	// Transaction Related Methods
    	// ---------------------------
    
    	#region Transaction Related Methods
    
    	/// <summary>
    	/// Marks the beginning of a transaction with a default Isolation Level of ReadUncommited.
    	/// </summary>
    	///
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public void BeginTransaction()
        {
            BeginTransaction(IsolationLevel.ReadCommitted);
        }
    
        /// <summary>
        /// Marks the beginning of a transaction.
        /// </summary>
        ///
        /// <param name="isolationLevel" type="IsolationLevel">The locking level in which to run this transaction.</param>
        /// 
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        public void BeginTransaction(IsolationLevel isolationLevel)
        {
            try
            {
    			if (CurrentConnection is null)
    				throw new NullReferenceException(nameof(CurrentConnection));
    
                if (CurrentCommand is null)
                    throw new NullReferenceException(nameof(CurrentCommand));
                
                //Primary Server
    			if (CurrentConnection.State is System.Data.ConnectionState.Closed)
    				CurrentConnection.Open();
    
    			CurrentTransaction = CurrentConnection.BeginTransaction(isolationLevel);
    			CurrentCommand.Transaction = CurrentTransaction;
    
                //Backup Server
                try
                {
    				if (ShouldUseBackupServer && CurrentBackupConnection is not null && CurrentBackupCommand is not null)
    				{
    					if (CurrentBackupConnection.State is System.Data.ConnectionState.Closed)
    						CurrentBackupConnection.Open();
    
    					CurrentBackupTransaction = CurrentBackupConnection.BeginTransaction(isolationLevel);
    					CurrentBackupCommand.Transaction = CurrentBackupTransaction;
    				}
    			}
								// Inside the catch block where you try to open the Backup Connection
                catch (Exception ex)
                {
                    // Log the backup connection failure, but don't rethrow (allow primary to continue)
                    ProcessException(ex, shouldRethrowException: false, isBackupException: true);
                }
                
    		}
            catch (Exception ex)
            {
                ProcessException(ex);
            }
        }
    
        public async Task BeginTransactionAsync(IsolationLevel isolationLevel)
        {
    		try
    		{
    			if (CurrentConnection is null)
    				throw new NullReferenceException(nameof(CurrentConnection));
    
    			if (CurrentCommand is null)
    				throw new NullReferenceException(nameof(CurrentCommand));
    
    			//Primary Server
    			if (CurrentConnection.State is System.Data.ConnectionState.Closed)
    				await CurrentConnection.OpenAsync();
    
    			CurrentTransaction = await CurrentConnection.BeginTransactionAsync(isolationLevel);
    			CurrentCommand.Transaction = CurrentTransaction;
    
                //Backup Server
                try
                {
    				if (ShouldUseBackupServer && CurrentBackupConnection is not null && CurrentBackupCommand is not null)
    				{
    					if (CurrentBackupConnection.State is System.Data.ConnectionState.Closed)
    						await CurrentBackupConnection.OpenAsync();
    
    					CurrentBackupTransaction = await CurrentBackupConnection.BeginTransactionAsync(isolationLevel);
    					CurrentBackupCommand.Transaction = CurrentBackupTransaction;
    				}
    			}
                catch (Exception ex)
                {
                    // Log the backup connection failure, but don't rethrow (allow primary to continue)
                    ProcessException(ex, shouldRethrowException: false, isBackupException: true);
                }
    		}
    		catch (Exception ex)
    		{
    			ProcessException(ex);
    		}
    	}
    
        /// <summary>
        /// Commits a transaction.
        /// </summary>
        /// 
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        public void CommitTransaction()
        {
            try
            {
    			if (CurrentTransaction is not null)
    			{
                    //Primary Server
    				CurrentTransaction.Commit();
    
                    //Backup Server
                    try
                    {
    					if (CurrentBackupTransaction is not null)
    					{
                            CurrentBackupTransaction.Commit();
    					}
    
    					CurrentBackupTransaction?.Dispose();
    					CurrentBackupTransaction = null;
    				}
                    catch (Exception ex)
                    {
                        // Log the backup connection failure, but don't rethrow (allow primary to continue)
                        ProcessException(ex, shouldRethrowException: false, isBackupException: true);
                    }
    			}
    
    			CurrentTransaction?.Dispose();
    			CurrentTransaction = null;
    		}
            catch (Exception ex)
            {
                ProcessException(ex);
            }
        }
    
    	/// <summary>
    	/// Commits a transaction.
    	/// </summary>
    	/// 
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public async Task CommitTransactionAsync()
        {
    		try
    		{
    			if (CurrentTransaction is not null)
    			{
    				//Primary Server
    				await CurrentTransaction.CommitAsync();
    
    				//Backup Server
    				try
    				{
    					if (CurrentBackupTransaction is not null)
    					{
    						await CurrentBackupTransaction.CommitAsync();
    					}
    
    					CurrentBackupTransaction?.Dispose();
    					CurrentBackupTransaction = null;
    				}
    				catch (Exception ex)
    				{
    					// Log the backup connection failure, but don't rethrow (allow primary to continue)
              ProcessException(ex, shouldRethrowException: false, isBackupException: true);
    				}
    			}
    
    			CurrentTransaction?.Dispose();
    			CurrentTransaction = null;
    		}
    		catch (Exception ex)
    		{
    			ProcessException(ex);
    		}
    	}
    
        /// <summary>
        /// Rollbacks a transaction.
        /// </summary>
        /// 
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        public void RollbackTransaction()
        {
            try
            {
                if (CurrentTransaction is not null)
                {
                    //Primary Server
                    CurrentTransaction.Rollback();
    
                    //Backup Server
                    try
                    {
    					if (CurrentBackupTransaction is not null)
    					{
    						CurrentBackupTransaction.Rollback();
    					}
    
    					CurrentBackupTransaction?.Dispose();
    					CurrentBackupTransaction = null;
    				}
                    catch (Exception ex)
                    {
                        // Log the backup connection failure, but don't rethrow (allow primary to continue)
                        ProcessException(ex, shouldRethrowException: false, isBackupException: true);
                    }
                }
    
    			CurrentTransaction?.Dispose();
    			CurrentTransaction = null;
    		}
            catch (Exception ex)
            {
                ProcessException(ex);
            }
        }
    
    	/// <summary>
    	/// Rollbacks a transaction.
    	/// </summary>
    	/// 
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public async Task RollbackTransactionAsync()
        {
    		try
    		{
    			if (CurrentTransaction is not null)
    			{
    				//Primary Server
    				await CurrentTransaction.RollbackAsync();
    
    				//Backup Server
    				try
    				{
    					if (CurrentBackupTransaction is not null)
    					{
    						await CurrentBackupTransaction.RollbackAsync();
    					}
    
    					CurrentBackupTransaction?.Dispose();
    					CurrentBackupTransaction = null;
    				}
    				catch (Exception ex)
    				{
    					// Log the backup connection failure, but don't rethrow (allow primary to continue)
              ProcessException(ex, shouldRethrowException: false, isBackupException: true);
    				}
    			}
    
                CurrentTransaction?.Dispose();
                CurrentTransaction = null;
    		}
    		catch (Exception ex)
    		{
    			ProcessException(ex);
    		}
    	}
    
        #endregion
    
        // -------------------------
        // Parameter Related Methods
        // -------------------------
    
        #region Parameter Related Methods
        /// <summary>
        /// Creates a new parameter on the current factory.
        /// </summary>
        ///	
        /// <returns>A DbParameter object representing a parameter to pass to a stored procedure.</returns>
        /// 
        /// <remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        public DbParameter? CreateParameter()
        {
            return ProviderFactory?.CreateParameter();
        }
    
        /// <summary>
        /// Adds a parameter to the call list prior to executing a stored procedure.
        /// </summary>
        ///
        /// <param name="parameter" type="DbParameter">The parameter to pass to the stored procedure.</param>
        ///	
        /// <returns>An integer representing the index of the added parameter.</returns>
        /// 
        /// <remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        public int AddParameter(DbParameter parameter)
        {
            if (CurrentCommand is null)
                return -1;
    
            var index = CurrentCommand.Parameters.Add(parameter);
    
            if (CurrentBackupCommand is not null)
						{
						    var clone = CloneParameter(parameter, CurrentBackupCommand);
						    CurrentBackupCommand.Parameters.Add(clone);
						}

						return index;
        }
    
    	/// <summary>
    	/// Adds a parameter to the call list prior to executing a stored procedure.
    	/// </summary>
    	///
    	/// <param name="name" type="string">The name of the parameter as defined in the stored procedure.</param>
    	/// <param name="value" type="object">The value of the parameter to pass to the stored procedure.</param> 
    	/// 
    	///	
    	/// <returns>An integer representing the index of the added parameter.</returns>
    	/// 
    	/// <remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public int AddParameter(string name, object value)
        {
            var p = ProviderFactory?.CreateParameter();
    
            if (p is null || CurrentCommand is null)
                return -1;
    
            p.ParameterName = name;
            p.Value = value;
    
            return AddParameter(p);
        }
    
    	/// <summary>
    	/// Adds a parameter to the current command parameters collection.
    	/// </summary>
    	///	
    	/// <param name="name" type="">The name of the parameter to add to the collection.</param>
    	/// <param name="value" type="">The value of the parameter.</param>
    	/// <param name="dataType" type="">The data type the parameter.</param>
    	/// 
    	/// <returns>An integer representing the index of the added parameter.</returns>
    	/// 
    	/// <remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author					        Date		    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public int AddParameter(string name, object value, DbType dataType)
    	{
    		var p = ProviderFactory?.CreateParameter();
    
    		if (p is null || CurrentCommand is null)
    			return -1;
    
    		p.ParameterName = name;
    		p.Value = value;
    		p.DbType = dataType;
    
    		return AddParameter(p);
    	}
    
    	/// <summary>
    	/// Adds a parameter to the current command parameters collection.
    	/// </summary>
    	///	
    	/// <param name="name" type="">The name of the parameter to add to the collection.</param>
    	/// <param name="value" type="">The value of the parameter.</param>
    	/// <param name="direction" type="">The direction the variable.</param>
    	/// 
    	/// <returns>An integer representing the index of the added parameter.</returns>
    	/// 
    	/// <remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public int AddParameter(string name, object value, ParameterDirection direction)
    	{
    		var p = ProviderFactory?.CreateParameter();
    
    		if (p is null || CurrentCommand is null)
    			return -1;
    
    		p.ParameterName = name;
    		p.Value = value;
    		p.Direction = direction;
    
    		return AddParameter(p);
    	}
    
    	/// <summary>
    	/// Adds a parameter to the current command parameters collection.
    	/// </summary>
    	///	
    	/// <param name="name" type="">The name of the parameter to add to the collection.</param>
    	/// <param name="value" type="">The value of the parameter.</param>
    	/// <param name="dataType" type="">The data type the parameter.</param>
    	/// <param name="direction" type="">The direction the variable.</param>
    	/// 
    	/// <returns>An integer representing the index of the added parameter.</returns>
    	/// 
    	/// <remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author					        Date		    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public int AddParameter(string name, object value, DbType dataType, ParameterDirection direction)
        {
    				var p = ProviderFactory?.CreateParameter();
    
    				if (p is null || CurrentCommand is null)
    					return -1;
    
    				p.ParameterName = name;
						p.Value = value;
						p.Direction = direction;
						p.DbType = dataType;
    
    				return AddParameter(p);
    	}
    
    	#endregion
    
    	// ------------------------
    	// NonQuery Related Methods
    	// ------------------------
    
    	#region NonQuery Related Methods
    
    	public ExecutionResult<int> ExecuteNonQuery(string spName)
        {
            return ExecuteNonQuery(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit);
        }
    
        public ExecutionResult<int> ExecuteNonQuery(string sqlStatement, CommandType commandType)
        {
            return ExecuteNonQuery(sqlStatement, commandType, ConnectionState.CloseOnExit);
        }
    
        public ExecutionResult<int> ExecuteNonQuery(string sqlStatement, CommandType commandType, ConnectionState connectionState)
        {
            if (CurrentCommand is null)
                throw new NullReferenceException(nameof(CurrentCommand));
    
    		if (CurrentConnection is null)
    			throw new NullReferenceException(nameof(CurrentConnection));
    
            var executionResult = new ExecutionResult<int>(0);
    
    		CurrentCommand.CommandText = sqlStatement;
            CurrentCommand.CommandType = commandType;
            CurrentCommand.CommandTimeout = CommandTimeOut;
    
            try
            {
                if (!TransactionHelper.IsInTransactionMode && CurrentConnection.State is System.Data.ConnectionState.Closed)
                {
                    CurrentConnection.Open();
                }
    
				LastAttemptedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
                
				var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
        
                if (TransactionHelper.IsInTransactionMode)
                    executionResult.Result = TransactionHelper.ExecuteNonQuery((DatabaseHelper)this.MemberwiseClone());
                else
    				executionResult.Result = CurrentCommand.ExecuteNonQuery();

                LastExecutedSQLTime = System.Diagnostics.Stopwatch.GetElapsedTime(startTime); // return the amount of time it took to execute
                LastExecutedSQL = LastAttemptedSQL;
				SaveSQLResponseTime(CurrentCommand.CommandText, LastExecutedSQLTime);
        
        if (ShouldUseBackupServer)
            _ = ExecuteNonQueryOnBackupServerAsync(sqlStatement, commandType, connectionState, CancellationToken.None)
                  .ContinueWith(t => ProcessException(t.Exception!.InnerException!, shouldRethrowException: false, isBackupException: true), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

            var backupExecutionResult = new ExecutionResult<int>(0);
    
    			executionResult.WasSuccessful = true;
            }
            catch (Exception ex)
            {
    			executionResult.WasSuccessful = false;
                ProcessException(ex);
            }
            finally
            {
                // Revision: MWO - 4-18-2013
                // Added sql statement logging 
                LogSQLStatementsIfNecessary(CurrentCommand);
    
                CurrentCommand.Parameters.Clear();
                if (connectionState is ConnectionState.CloseOnExit && CurrentCommand.Transaction is null)
                {
                    CurrentConnection.Close();
                }
            }
            return executionResult;
        }
    
    	public async Task<ExecutionResult<int>> ExecuteNonQueryAsync(string spName)
    	{
    		return await ExecuteNonQueryAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit);
    	}
		
		public async Task<ExecutionResult<int>> ExecuteNonQueryAsync(string spName, CancellationToken cancellationToken)
        {
            return await ExecuteNonQueryAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit, cancellationToken);
        }
    
    	public async Task<ExecutionResult<int>> ExecuteNonQueryAsync(string sqlStatement, CommandType commandType)
    	{
    		return await ExecuteNonQueryAsync(sqlStatement, commandType, ConnectionState.CloseOnExit);
    	}
		
		public async Task<ExecutionResult<int>> ExecuteNonQueryAsync(string sqlStatement, CommandType commandType, CancellationToken cancellationToken)
        {
            return await ExecuteNonQueryAsync(sqlStatement, commandType, ConnectionState.CloseOnExit, cancellationToken);
        }
    
    	public async Task<ExecutionResult<int>> ExecuteNonQueryAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState)
        {
			return await ExecuteNonQueryAsync(sqlStatement, commandType, connectionState, CancellationToken.None);
        }

        public async Task<ExecutionResult<int>> ExecuteNonQueryAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState, CancellationToken cancellationToken)
        {
    		if (CurrentCommand is null)
    			throw new NullReferenceException(nameof(CurrentCommand));
    
    		if (CurrentConnection is null)
    			throw new NullReferenceException(nameof(CurrentConnection));
    
    		var executionResult = new ExecutionResult<int>(0);
    
    		CurrentCommand.CommandText = sqlStatement;
    		CurrentCommand.CommandType = commandType;
    		CurrentCommand.CommandTimeout = CommandTimeOut;
    
    		try
    		{
    			if (!TransactionHelper.IsInTransactionMode && CurrentConnection.State is System.Data.ConnectionState.Closed)
    			{
    				await CurrentConnection.OpenAsync(cancellationToken);
    			}
				
				LastAttemptedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
    
    			var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
    			if (TransactionHelper.IsInTransactionMode)
    				executionResult.Result = await TransactionHelper.ExecuteNonQueryAsync((DatabaseHelper)this.MemberwiseClone());
    			else
    				executionResult.Result = await CurrentCommand.ExecuteNonQueryAsync(cancellationToken);
    			
				LastExecutedSQLTime = System.Diagnostics.Stopwatch.GetElapsedTime(startTime); // return the amount of time it took to execute
    			LastExecutedSQL = LastAttemptedSQL;
          
				SaveSQLResponseTime(CurrentCommand.CommandText, LastExecutedSQLTime);
        if (ShouldUseBackupServer)
            _ = ExecuteNonQueryOnBackupServerAsync(sqlStatement, commandType, connectionState, CancellationToken.None)
                  .ContinueWith(t => ProcessException(t.Exception!.InnerException!, shouldRethrowException: false, isBackupException: true), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    			executionResult.WasSuccessful = true;
    		}
    		catch (Exception ex)
    		{
    			executionResult.WasSuccessful = false;
    			ProcessException(ex);
    		}
    		finally
    		{
    			// Revision: MWO - 4-18-2013
    			// Added sql statement logging
    			LogSQLStatementsIfNecessary(CurrentCommand);
    
    			CurrentCommand.Parameters.Clear();
    
    			if (connectionState is ConnectionState.CloseOnExit && CurrentCommand.Transaction is null)
    			{
    				await CurrentConnection.CloseAsync();
    			}
    		}
    		return executionResult;
    	}
        
        #endregion
    
        #region NonQuery Related Methods (Backup Server)
    
        public ExecutionResult<int> ExecuteNonQueryOnBackupServer(string spName)
    	{
    		return ExecuteNonQueryOnBackupServer(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit);
    	}
    
    	public ExecutionResult<int> ExecuteNonQueryOnBackupServer(string sqlStatement, CommandType commandType)
    	{
    		return ExecuteNonQueryOnBackupServer(sqlStatement, commandType, ConnectionState.CloseOnExit);
    	}
    
    	public ExecutionResult<int> ExecuteNonQueryOnBackupServer(string sqlStatement, CommandType commandType, ConnectionState connectionState)
    	{
    		if (CurrentBackupCommand is null)
    			throw new NullReferenceException(nameof(CurrentBackupCommand));
    
    		if (CurrentBackupConnection is null)
    			throw new NullReferenceException(nameof(CurrentBackupConnection));
    
    		var executionResult = new ExecutionResult<int>(0);
    
    		CurrentBackupCommand.CommandText = sqlStatement;
    		CurrentBackupCommand.CommandType = commandType;
    		CurrentBackupCommand.CommandTimeout = CommandTimeOut;
    
    		try
    		{
    			if (!TransactionHelper.IsInTransactionMode && CurrentBackupConnection.State is System.Data.ConnectionState.Closed)
    			{
    				CurrentBackupConnection.Open();
    			}
          
          LastAttemptedBackupSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentBackupCommand, false);
    
    			if (TransactionHelper.IsInTransactionMode)
    				executionResult.Result = TransactionHelper.ExecuteNonQueryOnBackupServer((DatabaseHelper)this.MemberwiseClone());
    			else
    				executionResult.Result = CurrentBackupCommand.ExecuteNonQuery();
            
            LastExecutedBackupSQL = LastAttemptedBackupSQL;
    
    			executionResult.WasSuccessful = true;
    		}
    		catch (Exception ex)
    		{
    			executionResult.WasSuccessful = false;
    			// CHANGE: shouldRethrowException is false, isBackupException is true so we don't stop processing on primary server
          ProcessException(ex, shouldRethrowException: false, isBackupException: true);
    		}
    		finally
    		{
    			// Revision: MWO - 4-18-2013
    			// Added sql statement logging 
    			LogSQLStatementsIfNecessary(CurrentBackupCommand);
    
    			CurrentBackupCommand.Parameters.Clear();
          CurrentCommand?.Parameters.Clear(); //Clear on primary as well to keep in sync
    			if (connectionState is ConnectionState.CloseOnExit && CurrentBackupCommand.Transaction is null)
    			{
    				CurrentBackupConnection.Close();
    			}
    		}
    		return executionResult;
    	}
    
    	public async Task<ExecutionResult<int>> ExecuteNonQueryOnBackupServerAsync(string spName)
    	{
    		return await ExecuteNonQueryOnBackupServerAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit, CancellationToken.None);
    	}
		
		public async Task<ExecutionResult<int>> ExecuteNonQueryOnBackupServerAsync(string spName, CancellationToken cancellationToken)
        {
            return await ExecuteNonQueryOnBackupServerAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit, cancellationToken);
        }
    
    	public async Task<ExecutionResult<int>> ExecuteNonQueryOnBackupServerAsync(string sqlStatement, CommandType commandType)
    	{
    		return await ExecuteNonQueryOnBackupServerAsync(sqlStatement, commandType, ConnectionState.CloseOnExit, CancellationToken.None);
    	}
    
    	public async Task<ExecutionResult<int>> ExecuteNonQueryOnBackupServerAsync(string sqlStatement, CommandType commandType, CancellationToken cancellationToken)
        {
            return await ExecuteNonQueryOnBackupServerAsync(sqlStatement, commandType, ConnectionState.CloseOnExit, cancellationToken);
        }
        
        public async Task<ExecutionResult<int>> ExecuteNonQueryOnBackupServerAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState)
				{
				    return await ExecuteNonQueryOnBackupServerAsync(sqlStatement, commandType, connectionState, CancellationToken.None);
				}

        public async Task<ExecutionResult<int>> ExecuteNonQueryOnBackupServerAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState, CancellationToken cancellationToken)
        {
    		if (CurrentBackupCommand is null || CurrentBackupConnection is null)
    			throw new NullReferenceException(nameof(CurrentBackupCommand));
    
    		var executionResult = new ExecutionResult<int>(0);
    
    		CurrentBackupCommand.CommandText = sqlStatement;
    		CurrentBackupCommand.CommandType = commandType;
    		CurrentBackupCommand.CommandTimeout = CommandTimeOut;
    
    		try
    		{
    			if (!TransactionHelper.IsInTransactionMode && CurrentBackupConnection.State is System.Data.ConnectionState.Closed)
    			{
    				await CurrentBackupConnection.OpenAsync(cancellationToken);
    			}
          
          LastAttemptedBackupSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentBackupCommand, false);
    
					// TransactionHelper Async support preserved
    			if (TransactionHelper.IsInTransactionMode)
    				executionResult.Result = await TransactionHelper.ExecuteNonQueryOnBackupServerAsync((DatabaseHelper)this.MemberwiseClone());
    			else
    				executionResult.Result = await CurrentBackupCommand.ExecuteNonQueryAsync(cancellationToken);
    
				LastExecutedBackupSQL = LastAttemptedBackupSQL;
    
    			executionResult.WasSuccessful = true;
    		}
    		catch (Exception ex)
    		{
    			executionResult.WasSuccessful = false;
    			// CHANGE: shouldRethrowException is false, isBackupException is true so we don't stop processing on primary server
          ProcessException(ex, shouldRethrowException: false, isBackupException: true);
    		}
    		finally
    		{
    			LogSQLStatementsIfNecessary(CurrentBackupCommand);
    
    			CurrentBackupCommand.Parameters.Clear();
          CurrentCommand?.Parameters.Clear();
    			if (connectionState is ConnectionState.CloseOnExit && CurrentBackupCommand.Transaction is null)
    			{
    				await CurrentBackupConnection.CloseAsync();
    			}
    		}
    		return executionResult;
    	}
    
    	#endregion
    
    	// ----------------------
    	// Scalar Related Methods
    	// ----------------------
    
    	#region Scalar Related Methods
    
    	public ExecutionResult<object> ExecuteScalar(string spName)
        {
            return ExecuteScalar(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit);
        }
    
        public ExecutionResult<object> ExecuteScalar(string sqlStatement, CommandType commandType)
        {
            return ExecuteScalar(sqlStatement, commandType, ConnectionState.CloseOnExit);
        }
    
        public ExecutionResult<object> ExecuteScalar(string sqlStatement, CommandType commandType, ConnectionState connectionState)
        {
    		if (CurrentCommand is null)
    			throw new NullReferenceException(nameof(CurrentCommand));
    
    		if (CurrentConnection is null)
    			throw new NullReferenceException(nameof(CurrentConnection));
    
    		var executionResult = new ExecutionResult<object>();
    
    		CurrentCommand.CommandText = sqlStatement;
            CurrentCommand.CommandType = commandType;
            CurrentCommand.CommandTimeout = CommandTimeOut;
            
            try
            {
                if (!TransactionHelper.IsInTransactionMode && CurrentConnection.State is System.Data.ConnectionState.Closed)
                {
                    CurrentConnection.Open();
                }
				
				LastAttemptedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
    
                var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
                if (TransactionHelper.IsInTransactionMode)
                    executionResult.Result = TransactionHelper.ExecuteScalar((DatabaseHelper)this.MemberwiseClone());
                else
    				executionResult.Result = CurrentCommand.ExecuteScalar();
    
    			
				LastExecutedSQLTime = System.Diagnostics.Stopwatch.GetElapsedTime(startTime); // return the amount of time it took to execute
    			LastExecutedSQL = LastAttemptedSQL;
          
				SaveSQLResponseTime(CurrentCommand.CommandText, LastExecutedSQLTime);
    
				if (ShouldUseBackupServer)
						_ = ExecuteNonQueryOnBackupServerAsync(sqlStatement, commandType, connectionState, CancellationToken.None)
                              .ContinueWith(t => ProcessException(t.Exception!.InnerException!, shouldRethrowException: false, isBackupException: true), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    			executionResult.WasSuccessful = true;
            }
            catch (Exception ex)
            {
    			executionResult.WasSuccessful = false;
                ProcessException(ex);
            }
            finally
            {
                // Revision: MWO - 4-18-2013
                // Added sql statement logging
                LogSQLStatementsIfNecessary(CurrentCommand);
    
                CurrentCommand.Parameters.Clear();
    
                if (connectionState is ConnectionState.CloseOnExit && CurrentCommand.Transaction is null)
                {
                    CurrentConnection.Close();
                }
            }
    
            return executionResult;
        }
    
    	public async Task<ExecutionResult<object>> ExecuteScalarAsync(string spName)
    	{
    		return await ExecuteScalarAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit, CancellationToken.None);
    	}
		
		public async Task<ExecutionResult<object>> ExecuteScalarAsync(string spName, CancellationToken cancellationToken)
        {
            return await ExecuteScalarAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit, cancellationToken);
        }
    
    	public async Task<ExecutionResult<object>> ExecuteScalarAsync(string sqlStatement, CommandType commandType)
    	{
    		return await ExecuteScalarAsync(sqlStatement, commandType, ConnectionState.CloseOnExit, CancellationToken.None);
    	}
		
		public async Task<ExecutionResult<object>> ExecuteScalarAsync(string sqlStatement, CommandType commandType, CancellationToken cancellationToken)
        {
            return await ExecuteScalarAsync(sqlStatement, commandType, ConnectionState.CloseOnExit, cancellationToken);
        }
    
    	public async Task<ExecutionResult<object>> ExecuteScalarAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState)
        {
			return await ExecuteScalarAsync(sqlStatement, commandType, connectionState, CancellationToken.None);
        }

        public async Task<ExecutionResult<object>> ExecuteScalarAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState, CancellationToken cancellationToken)
        {
    		if (CurrentCommand is null)
    			throw new NullReferenceException(nameof(CurrentCommand));
    
    		if (CurrentConnection is null)
    			throw new NullReferenceException(nameof(CurrentConnection));
    
    		var executionResult = new ExecutionResult<object>();
    
    		CurrentCommand.CommandText = sqlStatement;
    		CurrentCommand.CommandType = commandType;
    		CurrentCommand.CommandTimeout = CommandTimeOut;
    
    		try
    		{
    			if (!TransactionHelper.IsInTransactionMode && CurrentConnection.State is System.Data.ConnectionState.Closed)
    			{
    				await CurrentConnection!.OpenAsync(cancellationToken);
    			}
				
				LastAttemptedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
    
    			var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
    			if (TransactionHelper.IsInTransactionMode)
    				executionResult.Result = await TransactionHelper.ExecuteScalarAsync((DatabaseHelper)this.MemberwiseClone());
    			else
    				executionResult.Result = await CurrentCommand!.ExecuteScalarAsync(cancellationToken);
    
    			
				LastExecutedSQLTime = System.Diagnostics.Stopwatch.GetElapsedTime(startTime); // return the amount of time it took to execute
    			LastExecutedSQL = LastAttemptedSQL;
          
				SaveSQLResponseTime(CurrentCommand.CommandText, LastExecutedSQLTime);
        
        if (ShouldUseBackupServer)
						_ = ExecuteNonQueryOnBackupServerAsync(sqlStatement, commandType, connectionState, CancellationToken.None)
                              .ContinueWith(t => ProcessException(t.Exception!.InnerException!, shouldRethrowException: false, isBackupException: true), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    			executionResult.WasSuccessful = true;
    		}
    		catch (Exception ex)
    		{
    			executionResult.WasSuccessful = false;
    			ProcessException(ex);
    		}
    		finally
    		{
    			// Revision: MWO - 4-18-2013
    			// Added sql statement logging
    			LogSQLStatementsIfNecessary(CurrentCommand);

    			CurrentCommand.Parameters.Clear();

    			if (connectionState is ConnectionState.CloseOnExit && CurrentCommand.Transaction is null)
    			{
    				await CurrentConnection.CloseAsync();
    			}
    		}

    		return executionResult;
    	}

    	#endregion

    	#region Scalar Related Methods (Backup Server)
    
    	public ExecutionResult<object> ExecuteScalarOnBackupServer(string spName)
    	{
    		return ExecuteScalarOnBackupServer(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit);
    	}
    
    	public ExecutionResult<object> ExecuteScalarOnBackupServer(string sqlStatement, CommandType commandType)
    	{
    		return ExecuteScalarOnBackupServer(sqlStatement, commandType, ConnectionState.CloseOnExit);
    	}
    
    	public ExecutionResult<object> ExecuteScalarOnBackupServer(string sqlStatement, CommandType commandType, ConnectionState connectionState)
    	{
    		if (CurrentBackupCommand is null)
    			throw new NullReferenceException(nameof(CurrentBackupCommand));
    
    		if (CurrentBackupConnection is null)
    			throw new NullReferenceException(nameof(CurrentBackupConnection));
    
    		var executionResult = new ExecutionResult<object>();
    
    		CurrentBackupCommand.CommandText = sqlStatement;
    		CurrentBackupCommand.CommandType = commandType;
    		CurrentBackupCommand.CommandTimeout = CommandTimeOut;
    
    		try
    		{
    			if (!TransactionHelper.IsInTransactionMode && CurrentBackupConnection.State is System.Data.ConnectionState.Closed)
    			{
    				CurrentBackupConnection.Open();
    			}
    
    			LastAttemptedBackupSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentBackupCommand, false);
    			if (TransactionHelper.IsInTransactionMode)
    				executionResult.Result = TransactionHelper.ExecuteScalarOnBackupServer((DatabaseHelper)this.MemberwiseClone());
    			else
    				executionResult.Result = CurrentBackupCommand.ExecuteScalar();
            
            LastExecutedBackupSQL = LastAttemptedBackupSQL;
    
    			executionResult.WasSuccessful = true;
    		}
    		catch (Exception ex)
    		{
    			executionResult.WasSuccessful = false;
    			ProcessException(ex, shouldRethrowException: false, isBackupException: true);
    		}
    		finally
    		{
    			// Revision: MWO - 4-18-2013
    			// Added sql statement logging 
    			LogSQLStatementsIfNecessary(CurrentBackupCommand);
    
    			CurrentBackupCommand.Parameters.Clear();
          CurrentCommand?.Parameters.Clear(); //Clear on primary as well to keep in sync
    
    			if (connectionState is ConnectionState.CloseOnExit && CurrentBackupCommand.Transaction is null)
    			{
    				CurrentBackupConnection.Close();
    			}
    		}
    
    		return executionResult;
    	}
    
    	public async Task<ExecutionResult<object>> ExecuteScalarOnBackupServerAsync(string spName)
    	{
    		return await ExecuteScalarOnBackupServerAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit, CancellationToken.None);
    	}
		
		public async Task<ExecutionResult<object>> ExecuteScalarOnBackupServerAsync(string spName, CancellationToken cancellationToken)
        {
            return await ExecuteScalarOnBackupServerAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit, cancellationToken);
        }
    
    	public async Task<ExecutionResult<object>> ExecuteScalarOnBackupServerAsync(string sqlStatement, CommandType commandType)
    	{
    		return await ExecuteScalarOnBackupServerAsync(sqlStatement, commandType, ConnectionState.CloseOnExit, CancellationToken.None);
    	}
		
		public async Task<ExecutionResult<object>> ExecuteScalarOnBackupServerAsync(string sqlStatement, CommandType commandType, CancellationToken cancellationToken)
        {
            return await ExecuteScalarOnBackupServerAsync(sqlStatement, commandType, ConnectionState.CloseOnExit, cancellationToken);
        }
    
    	public async Task<ExecutionResult<object>> ExecuteScalarOnBackupServerAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState)
    	{
			return await ExecuteScalarOnBackupServerAsync(sqlStatement, commandType, connectionState, CancellationToken.None);
        }

        public async Task<ExecutionResult<object>> ExecuteScalarOnBackupServerAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState, CancellationToken cancellationToken)
        {
    		if (CurrentBackupCommand is null)
    			throw new NullReferenceException(nameof(CurrentBackupCommand));
    
    		if (CurrentBackupConnection is null)
    			throw new NullReferenceException(nameof(CurrentBackupConnection));
    
    		var executionResult = new ExecutionResult<object>();
    
    		CurrentBackupCommand.CommandText = sqlStatement;
    		CurrentBackupCommand.CommandType = commandType;
    		CurrentBackupCommand.CommandTimeout = CommandTimeOut;
    
    		try
    		{
    			if (!TransactionHelper.IsInTransactionMode && CurrentBackupConnection.State is System.Data.ConnectionState.Closed)
    			{
    				await CurrentBackupConnection.OpenAsync(cancellationToken);
    			}
    
    			LastAttemptedBackupSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentBackupCommand, false);
    			if (TransactionHelper.IsInTransactionMode)
    				executionResult.Result = await TransactionHelper.ExecuteScalarOnBackupServerAsync((DatabaseHelper)this.MemberwiseClone());
    			else
    				executionResult.Result = await CurrentBackupCommand.ExecuteScalarAsync(cancellationToken);
            
          LastExecutedBackupSQL = LastAttemptedBackupSQL;
    
    			executionResult.WasSuccessful = true;
    		}
    		catch (Exception ex)
    		{
    			executionResult.WasSuccessful = false;
    			ProcessException(ex, shouldRethrowException: false, isBackupException: true);
    		}
    		finally
    		{
    			// Revision: MWO - 4-18-2013
    			// Added sql statement logging 
    			LogSQLStatementsIfNecessary(CurrentBackupCommand);
    
    			CurrentBackupCommand.Parameters.Clear();
          CurrentCommand?.Parameters.Clear(); //Clear on primary as well to keep in sync
    
    			if (connectionState is ConnectionState.CloseOnExit && CurrentBackupCommand.Transaction is null)
    			{
    				await CurrentBackupConnection.CloseAsync();
    			}
    		}
    
    		return executionResult;
    	}
    
    	#endregion
    
    	// -----------------------------
    	// ExecuteReader Related Methods
    	// -----------------------------
    
    	#region ExecuteReader Related Methods
    
    	public ExecutionResult<DbDataReader> ExecuteReader(string spName)
        {
            return ExecuteReader(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit);
        }
    
        public ExecutionResult<DbDataReader> ExecuteReader(string sqlStatement, CommandType commandType)
        {
            return ExecuteReader(sqlStatement, commandType, ConnectionState.CloseOnExit);
        }
    
        public ExecutionResult<DbDataReader> ExecuteReader(string sqlStatement, CommandType commandType, ConnectionState connectionState)
        {
    		if (CurrentCommand is null)
    			throw new NullReferenceException(nameof(CurrentCommand));
    
    		if (CurrentConnection is null)
    			throw new NullReferenceException(nameof(CurrentConnection));
    
    		var executionResult = new ExecutionResult<DbDataReader>();
    
    		CurrentCommand.CommandText = sqlStatement;
            CurrentCommand.CommandType = commandType;
            CurrentCommand.CommandTimeout = CommandTimeOut;
    
            try
            {
                if (!TransactionHelper.IsInTransactionMode && CurrentConnection.State is System.Data.ConnectionState.Closed)
                {
                    CurrentConnection.Open();
                }
				
				LastAttemptedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
    
                var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
                
                if (connectionState is ConnectionState.CloseOnExit)
                {
                    if (TransactionHelper.IsInTransactionMode)
                        CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
                    else
                        CurrentDataReader = CurrentCommand.ExecuteReader(CommandBehavior.CloseConnection);
                }
                else
                {
                    if (TransactionHelper.IsInTransactionMode)
                        CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
                    else
                        CurrentDataReader = CurrentCommand.ExecuteReader();
                }
    			
				LastExecutedSQLTime = System.Diagnostics.Stopwatch.GetElapsedTime(startTime); // return the amount of time it took to execute
    			LastExecutedSQL = LastAttemptedSQL;
				SaveSQLResponseTime(CurrentCommand.CommandText, LastExecutedSQLTime);

				if (ShouldUseBackupServer)
						_ = ExecuteNonQueryOnBackupServerAsync(sqlStatement, commandType, connectionState, CancellationToken.None)
                              .ContinueWith(t => ProcessException(t.Exception!.InnerException!, shouldRethrowException: false, isBackupException: true), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    			executionResult.WasSuccessful = true;
                executionResult.Result = CurrentDataReader;
            }
            catch (Exception ex)
            {
    			executionResult.WasSuccessful = false;
                ProcessException(ex);
            }
            finally
            {
                // Revision: MWO - 4-18-2013
                // Added sql statement logging 
                LogSQLStatementsIfNecessary(CurrentCommand);
    
                CurrentCommand.Parameters.Clear();
            }
    
            return executionResult;
        }
    
    	public async Task<ExecutionResult<DbDataReader>> ExecuteReaderAsync(string spName)
    	{
    		return await ExecuteReaderAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit, CancellationToken.None);
    	}
		
		public async Task<ExecutionResult<DbDataReader>> ExecuteReaderAsync(string spName, CancellationToken cancellationToken)
        {
            return await ExecuteReaderAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit, cancellationToken);
        }
    
    	public async Task<ExecutionResult<DbDataReader>> ExecuteReaderAsync(string sqlStatement, CommandType commandType)
    	{
    		return await ExecuteReaderAsync(sqlStatement, commandType, ConnectionState.CloseOnExit, CancellationToken.None);
    	}
		
		public async Task<ExecutionResult<DbDataReader>> ExecuteReaderAsync(string sqlStatement, CommandType commandType, CancellationToken cancellationToken)
        {
            return await ExecuteReaderAsync(sqlStatement, commandType, ConnectionState.CloseOnExit, cancellationToken);
        }
    
    	public async Task<ExecutionResult<DbDataReader>> ExecuteReaderAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState)
        {
			return await ExecuteReaderAsync(sqlStatement, commandType, connectionState, CancellationToken.None);
        }

        public async Task<ExecutionResult<DbDataReader>> ExecuteReaderAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState, CancellationToken cancellationToken)
        {
            if (CurrentCommand is null)
    			throw new NullReferenceException(nameof(CurrentCommand));
    
    		if (CurrentConnection is null)
    			throw new NullReferenceException(nameof(CurrentConnection));
    
    		var executionResult = new ExecutionResult<DbDataReader>();
    
    		CurrentCommand.CommandText = sqlStatement;
            CurrentCommand.CommandType = commandType;
            CurrentCommand.CommandTimeout = CommandTimeOut;
    
            try
            {
                if (!TransactionHelper.IsInTransactionMode && CurrentConnection.State is System.Data.ConnectionState.Closed)
                {
                    await CurrentConnection.OpenAsync(cancellationToken);
                }
				
				LastAttemptedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
    
                var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
                if (connectionState is ConnectionState.CloseOnExit)
                {
                    if (TransactionHelper.IsInTransactionMode)
                        CurrentDataReader = await TransactionHelper.ExecuteReaderAsync((DatabaseHelper)this.MemberwiseClone());
                    else
                        CurrentDataReader = await CurrentCommand.ExecuteReaderAsync(CommandBehavior.CloseConnection, cancellationToken);
                }
                else
                {
                    if (TransactionHelper.IsInTransactionMode)
                        CurrentDataReader = await TransactionHelper.ExecuteReaderAsync((DatabaseHelper)this.MemberwiseClone());
                    else
                        CurrentDataReader = await CurrentCommand.ExecuteReaderAsync(cancellationToken);
                }
    
    			
				LastExecutedSQLTime = System.Diagnostics.Stopwatch.GetElapsedTime(startTime); // return the amount of time it took to execute
    			LastExecutedSQL = LastAttemptedSQL;
				SaveSQLResponseTime(CurrentCommand.CommandText, LastExecutedSQLTime);
        
        if (ShouldUseBackupServer)
						_ = ExecuteNonQueryOnBackupServerAsync(sqlStatement, commandType, connectionState, CancellationToken.None)
                              .ContinueWith(t => ProcessException(t.Exception!.InnerException!, shouldRethrowException: false, isBackupException: true), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    			executionResult.WasSuccessful = true;
                executionResult.Result = CurrentDataReader;
            }
            catch (Exception ex)
            {
    			executionResult.WasSuccessful = false;
                ProcessException(ex);
            }
            finally
            {
                // Revision: MWO - 4-18-2013
                // Added sql statement logging 
                LogSQLStatementsIfNecessary(CurrentCommand);
    
                CurrentCommand.Parameters.Clear();
            }
    
            return executionResult;
        }
    	#endregion
    
    	#region ExecuteReader Related Methods (Backup Server)
    
    	public ExecutionResult<DbDataReader> ExecuteReaderOnBackupServer(string spName)
    	{
    		return ExecuteReaderOnBackupServer(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit);
    	}
    
    	public ExecutionResult<DbDataReader> ExecuteReaderOnBackupServer(string sqlStatement, CommandType commandType)
    	{
    		return ExecuteReaderOnBackupServer(sqlStatement, commandType, ConnectionState.CloseOnExit);
    	}
    
    	public ExecutionResult<DbDataReader> ExecuteReaderOnBackupServer(string sqlStatement, CommandType commandType, ConnectionState connectionState)
    	{
    		if (CurrentBackupCommand is null)
    			throw new NullReferenceException(nameof(CurrentBackupCommand));
    
    		if (CurrentBackupConnection is null)
    			throw new NullReferenceException(nameof(CurrentBackupConnection));
    
    		var executionResult = new ExecutionResult<DbDataReader>();
    
    		CurrentBackupCommand.CommandText = sqlStatement;
    		CurrentBackupCommand.CommandType = commandType;
    		CurrentBackupCommand.CommandTimeout = CommandTimeOut;
    
    		try
    		{
    			if (!TransactionHelper.IsInTransactionMode && CurrentBackupConnection.State is System.Data.ConnectionState.Closed)
    			{
    				CurrentBackupConnection.Open();
    			}
    
    			LastAttemptedBackupSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentBackupCommand, false);
          
    			if (connectionState is ConnectionState.CloseOnExit)
    			{
    				if (TransactionHelper.IsInTransactionMode)
    					CurrentBackupDataReader = TransactionHelper.ExecuteReaderOnBackupServer((DatabaseHelper)this.MemberwiseClone());
    				else
    					CurrentBackupDataReader = CurrentBackupCommand.ExecuteReader(CommandBehavior.CloseConnection);
    			}
    			else
    			{
    				if (TransactionHelper.IsInTransactionMode)
    					CurrentBackupDataReader = TransactionHelper.ExecuteReaderOnBackupServer((DatabaseHelper)this.MemberwiseClone());
    				else
    					CurrentBackupDataReader = CurrentBackupCommand.ExecuteReader();
    			}
          
          LastExecutedBackupSQL = LastAttemptedBackupSQL;
    
    			executionResult.WasSuccessful = true;
    			executionResult.Result = CurrentBackupDataReader;
    		}
    		catch (Exception ex)
    		{
    			executionResult.WasSuccessful = false;
    			ProcessException(ex, shouldRethrowException: false, isBackupException: true);
    		}
    		finally
    		{
    			// Revision: MWO - 4-18-2013
    			// Added sql statement logging 
    			LogSQLStatementsIfNecessary(CurrentBackupCommand);
    
    			CurrentBackupCommand.Parameters.Clear();
          CurrentCommand?.Parameters.Clear(); //Clear on primary as well to keep in sync
    		}
    
    		return executionResult;
    	}
    
    	public async Task<ExecutionResult<DbDataReader>> ExecuteReaderOnBackupServerAsync(string spName)
    	{
    		return await ExecuteReaderOnBackupServerAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit, CancellationToken.None);
    	}
		
		public async Task<ExecutionResult<DbDataReader>> ExecuteReaderOnBackupServerAsync(string spName, CancellationToken cancellationToken)
        {
            return await ExecuteReaderOnBackupServerAsync(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit, cancellationToken);
        }
    
    	public async Task<ExecutionResult<DbDataReader>> ExecuteReaderOnBackupServerAsync(string sqlStatement, CommandType commandType)
    	{
    		return await ExecuteReaderOnBackupServerAsync(sqlStatement, commandType, ConnectionState.CloseOnExit, CancellationToken.None);
    	}
    
    	public async Task<ExecutionResult<DbDataReader>> ExecuteReaderOnBackupServerAsync(string sqlStatement, CommandType commandType, CancellationToken cancellationToken)
        {
            return await ExecuteReaderOnBackupServerAsync(sqlStatement, commandType, ConnectionState.CloseOnExit, cancellationToken);
        }
        
        public async Task<ExecutionResult<DbDataReader>> ExecuteReaderOnBackupServerAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState)
				{
				    return await ExecuteReaderOnBackupServerAsync(sqlStatement, commandType, connectionState, CancellationToken.None);
				}

        public async Task<ExecutionResult<DbDataReader>> ExecuteReaderOnBackupServerAsync(string sqlStatement, CommandType commandType, ConnectionState connectionState, CancellationToken cancellationToken)
        {
    		if (CurrentBackupCommand is null)
    			throw new NullReferenceException(nameof(CurrentBackupCommand));
    
    		if (CurrentBackupConnection is null)
    			throw new NullReferenceException(nameof(CurrentBackupConnection));
    
    		var executionResult = new ExecutionResult<DbDataReader>();
    
    		CurrentBackupCommand.CommandText = sqlStatement;
    		CurrentBackupCommand.CommandType = commandType;
    		CurrentBackupCommand.CommandTimeout = CommandTimeOut;
    
    		try
    		{
    			if (!TransactionHelper.IsInTransactionMode && CurrentBackupConnection.State is System.Data.ConnectionState.Closed)
    			{
    				await CurrentBackupConnection.OpenAsync(cancellationToken);
    			}
    
    			LastAttemptedBackupSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentBackupCommand, false);
          
    			if (connectionState is ConnectionState.CloseOnExit)
    			{
    				if (TransactionHelper.IsInTransactionMode)
    					CurrentBackupDataReader = await TransactionHelper.ExecuteReaderOnBackupServerAsync((DatabaseHelper)this.MemberwiseClone());
    				else
    					CurrentBackupDataReader = await CurrentBackupCommand.ExecuteReaderAsync(CommandBehavior.CloseConnection, cancellationToken);
    			}
    			else
    			{
    				if (TransactionHelper.IsInTransactionMode)
    					CurrentBackupDataReader = await TransactionHelper.ExecuteReaderOnBackupServerAsync((DatabaseHelper)this.MemberwiseClone());
    				else
    					CurrentBackupDataReader = await CurrentBackupCommand.ExecuteReaderAsync(cancellationToken);
    			}
          
          LastExecutedBackupSQL = LastAttemptedBackupSQL;
    
    			executionResult.WasSuccessful = true;
    			executionResult.Result = CurrentBackupDataReader;
    		}
    		catch (Exception ex)
    		{
    			executionResult.WasSuccessful = false;
    			ProcessException(ex, shouldRethrowException: false, isBackupException: true);
    		}
    		finally
    		{
    			// Revision: MWO - 4-18-2013
    			// Added sql statement logging 
    			LogSQLStatementsIfNecessary(CurrentBackupCommand);
    
    			CurrentBackupCommand.Parameters.Clear();
          CurrentCommand?.Parameters.Clear(); //Clear on primary as well to keep in sync
    		}
    
    		return executionResult;
    	}
    	#endregion
    
    	// ------------------------
    	// DataSet Related Methods
    	// ------------------------
    
    	#region DataSet Related Methods
    
    	public ExecutionResult<DataSet> ExecuteDataSet(string spName)
        {
            return ExecuteDataSet(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit);
        }
    
        public ExecutionResult<DataSet> ExecuteDataSet(string sqlStatement, CommandType commandType)
        {
            return ExecuteDataSet(sqlStatement, commandType, ConnectionState.CloseOnExit);
        }
    
        public ExecutionResult<DataSet> ExecuteDataSet(string sqlStatement, CommandType commandType, ConnectionState connectionState)
        {
    		if (CurrentCommand is null)
    			throw new NullReferenceException(nameof(CurrentCommand));
    
    		if (CurrentConnection is null)
    			throw new NullReferenceException(nameof(CurrentConnection));
    
            if (ProviderFactory is null)
                throw new NullReferenceException(nameof(ProviderFactory));
            
            var executionResult = new ExecutionResult<DataSet>(new DataSet());
    
            CurrentCommand.CommandText = sqlStatement;
            CurrentCommand.CommandType = commandType;
            CurrentCommand.CommandTimeout = CommandTimeOut;
    
            var adapter = ProviderFactory.CreateDataAdapter();
            if (adapter is null)
                return executionResult;
    
            adapter.SelectCommand = CurrentCommand;
    
            try
            {
				LastAttemptedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
			
                var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
                if (TransactionHelper.IsInTransactionMode)
                    executionResult.Result = TransactionHelper.ExecuteDataSet(this, adapter);
                else
                    adapter.Fill(executionResult.Result!);
    
    			
				LastExecutedSQLTime = System.Diagnostics.Stopwatch.GetElapsedTime(startTime); // return the amount of time it took to execute
    			LastExecutedSQL = LastAttemptedSQL;
          
				SaveSQLResponseTime(CurrentCommand.CommandText, LastExecutedSQLTime);

				if (ShouldUseBackupServer)
            _ = ExecuteNonQueryOnBackupServerAsync(sqlStatement, commandType, connectionState, CancellationToken.None)
                  .ContinueWith(t => ProcessException(t.Exception!.InnerException!, shouldRethrowException: false, isBackupException: true), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    			executionResult.WasSuccessful = true;
            }
            catch (Exception ex)
            {
    			executionResult.WasSuccessful = false;
                ProcessException(ex);
            }
            finally
            {

                // Revision: MWO - 4-18-2013
                // Added sql statement logging 
                LogSQLStatementsIfNecessary(CurrentCommand);
    
                CurrentCommand.Parameters.Clear();
                if (connectionState is ConnectionState.CloseOnExit)
                {
                    if (CurrentConnection.State is System.Data.ConnectionState.Open)
                    {
                        CurrentConnection.Close();
                    }
                }
            }
            return executionResult;
        }
    
    	#endregion
    
    	#region DataSet Related Methods (Backup Server)
    
    	public ExecutionResult<DataSet> ExecuteDataSetOnBackupServer(string spName)
    	{
    		return ExecuteDataSetOnBackupServer(spName, CommandType.StoredProcedure, ConnectionState.CloseOnExit);
    	}
    
    	public ExecutionResult<DataSet> ExecuteDataSetOnBackupServer(string sqlStatement, CommandType commandType)
    	{
    		return ExecuteDataSetOnBackupServer(sqlStatement, commandType, ConnectionState.CloseOnExit);
    	}
    
    	public ExecutionResult<DataSet> ExecuteDataSetOnBackupServer(string sqlStatement, CommandType commandType, ConnectionState connectionState)
    	{
    		if (CurrentBackupCommand is null)
    			throw new NullReferenceException(nameof(CurrentBackupCommand));
    
    		if (CurrentBackupConnection is null)
    			throw new NullReferenceException(nameof(CurrentBackupConnection));
    
    		if (ProviderFactory is null)
            throw new NullReferenceException(nameof(ProviderFactory));
    
    		var executionResult = new ExecutionResult<DataSet>(new DataSet());
    
    		CurrentBackupCommand.CommandText = sqlStatement;
    		CurrentBackupCommand.CommandType = commandType;
    		CurrentBackupCommand.CommandTimeout = CommandTimeOut;
    
    		var adapter = ProviderFactory.CreateDataAdapter();
    		if (adapter is null)
    			return executionResult;
    
    		adapter.SelectCommand = CurrentBackupCommand;
    
    		try
    		{
    			LastAttemptedBackupSQL  = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentBackupCommand, false);
          
    			if (TransactionHelper.IsInTransactionMode)
    				executionResult.Result = TransactionHelper.ExecuteDataSetOnBackupServer(this, adapter);
    			else
    				adapter.Fill(executionResult.Result!);
            
         LastExecutedBackupSQL = LastAttemptedBackupSQL;   
    
    			executionResult.WasSuccessful = true;
    		}
    		catch (Exception ex)
    		{
    			executionResult.WasSuccessful = false;
    			ProcessException(ex, shouldRethrowException: false, isBackupException: true);
    		}
    		finally
    		{
    
    			// Revision: MWO - 4-18-2013
    			// Added sql statement logging 
    			LogSQLStatementsIfNecessary(CurrentBackupCommand);
    
    			CurrentBackupCommand.Parameters.Clear();
          CurrentCommand?.Parameters.Clear(); //Clear on primary as well to keep in sync
          
    			if (connectionState is ConnectionState.CloseOnExit)
    			{
    				if (CurrentBackupConnection.State is System.Data.ConnectionState.Open)
    				{
    					CurrentBackupConnection.Close();
    				}
    			}
    		}
    		return executionResult;
    	}
    
    	#endregion
    
    	// --------------
    	// Helper Methods
    	// --------------
    
    	#region Helper Methods
    
    	/// <summary>
    	/// This method determines if an exception should be recorded, records it (if needed) and rethrows the exception.
    	/// </summary>
    	///
    	/// <param name="ex" type="Exception">The exception object</param>
    	/// <param name="rethrowException" type="bool">Should rethrows the exception</param>        
    	/// 
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public void ProcessException(Exception ex, bool shouldRethrowException = true, bool isBackupException = false)
    	{
    		if (ShouldLogErrors && ex is not null)
            {
                // Standard DLG Error Logging
                WriteToLog($"Config File Used: {ConfigurationHelper.ConfigFileUsed}");
                WriteToLog($"Connection String Used: {(isBackupException ? this.BackupConnectionString : this.ConnectionString)}");
                WriteToLog(ex.Message);

                if (isBackupException)
                {
                    WriteToLog($"Last SQL Statement Attempted: {this.LastAttemptedBackupSQL}");

                    // NEW: Log the actual failing SQL statement to a script file
                    if (!string.IsNullOrWhiteSpace(this.LastAttemptedBackupSQL))
                    {
                        StringBuilder sqlLog = new StringBuilder();
                        sqlLog.AppendLine($"-- FAILED BACKUP SQL - Recorded at: {DateTime.Now:MM/dd/yyyy hh:mm:ss tt}");
                        sqlLog.AppendLine(this.LastAttemptedBackupSQL.Trim());

                        _singletonLogWriter.WriteText(sqlLog.ToString(), FAILED_BACKUP_DB_SQL_STATEMENTS_LOG_FILE, false);
                        
                    }
                }
                else
                {
                    WriteToLog($"Last SQL Statement Attempted: {this.LastAttemptedSQL}");
                    WriteToLog($"Last SQL Statement Executed: {this.LastExecutedSQL}");
                }
            }

            if (shouldRethrowException && ex is not null)
                ExceptionDispatchInfo.Capture(ex).Throw();
    	}
    
    	/// <summary>
    	/// This method extracts the user used in connection string.
    	/// </summary>
    	///
    	/// <param name="connectionString" type="string">Connection string.</param>
    	/// 
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public static string GetUserIdFromConnectionString(string connectionString)
    	{
    		var userName = "Windows Authentication";
    
    		var db = new DbConnectionStringBuilder();
    		db.ConnectionString = connectionString;
    		if (db.ContainsKey("user id"))
    		{
    			userName = db["user id"].ToString() ?? string.Empty;
    		}
    
    		return userName;
    	}
    
    	/// <summary>
    	/// This method extracts the server used in connection string.
    	/// </summary>
    	///
    	/// <param name="connectionString" type="string">Connection string.</param>
    	/// 
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public static string GetServerFromConnectionString(string connectionString)
    	{
    		if (string.IsNullOrWhiteSpace(connectionString))
    		{
    			return string.Empty;
    		}
    
    		var serverName = string.Empty;
    
    		var db = new DbConnectionStringBuilder();
    		db.ConnectionString = connectionString;
    		if (db.ContainsKey("server"))
    		{
    			serverName = db["server"]?.ToString() ?? string.Empty;
    		}
    		else if (db.ContainsKey("data source"))
    		{
    			serverName = db["data source"]?.ToString() ?? string.Empty;
    		}
    
    		return serverName;
    	}
    
    
    	/// <summary>
    	/// This method extracts the server used in connection string.
    	/// </summary>
    	///
    	/// <param name="connectionString" type="string">Connection string.</param>
    	/// 
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public static string GetDatabaseNameFromConnectionString(string connectionString)
    	{
    		var databaseName = string.Empty;
    
    		var db = new DbConnectionStringBuilder();
    		db.ConnectionString = connectionString;
    		if (db.ContainsKey("database"))
    		{
    			databaseName = db["database"].ToString() ?? string.Empty;
    		}
    		else if (db.ContainsKey("initial catalog"))
    		{
    			databaseName = db["initial catalog"].ToString() ?? string.Empty;
    		}
    
    		return databaseName;
    	}
    
    	/// <summary>
    	/// This method generates a sequential GUID.
    	/// </summary>
    	///
    	/// <returns>A sequential GUID.</returns>
    	/// 
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>
    	public static Guid GetSequentialGUID()
    	{
    		var newGuid = new Guid();
    		_ = UuidCreateSequential(ref newGuid);
    		return newGuid;
    	}
    
    	/// <summary>
    	/// This method copies the content of one object to another regardless of type.
    	/// </summary>
    	///
    	/// <returns>A number of properties affected.</returns>
    	/// 
    	/// <param name="from" type = "object">The reference to the source object.</param>
    	/// <param name="to" type = "object">The reference to the result pobject.</param>
    	/// <param name="ignoreProperties" type = "string">A simple comma delimited string of properties to ignore when copying.</param>
    	///	<remarks>
    	///	
    	/// <RevisionHistory>
    	/// Author				Date			                    Description
    	/// DLGenerator			6/4/2026 10:07:11 PM				Created function
    	/// 
    	/// </RevisionHistory>
    	/// 
    	/// </remarks>        
    	public static int CopyProperties(object from, object to, string ignoreProperties = "")
        {
            var nCount = 0;
            var dictionary = DictionaryFromType(from);
            ignoreProperties += ",";
    
            var t = to.GetType();
            var props = t.GetProperties();
            foreach (var prop in props)
            {
                try
                {
                    if (ignoreProperties.ToLower().IndexOf(prop.Name.ToLower() + ",") < 0)
                    {
                        if (dictionary.ContainsKey(prop.Name))
                            prop.SetValue(to, dictionary[prop.Name], null);
                        nCount++;
                    }
                }
                catch (Exception)
                {
                }
            }
            return nCount;
        }
    
		private static Dictionary<string, object> DictionaryFromType(object atype)
		{
		    if (atype is null) return [];

		    var t = atype.GetType();
		    var props = t.GetProperties();
		    var dict = new Dictionary<string, object>();
		    foreach (var prp in props)
		    {
		        var value = prp.GetValue(atype, []);
		        dict.Add(prp.Name, value);
		    }
		    return dict;
		}
		
		private void SaveSQLResponseTime(string commandText, TimeSpan executionTime)
        {
            DatabaseHelper? dh = null;
            try
            {

                if (ShouldSaveSQLResponseTimes && !TransactionHelper.IsInTransactionMode)
                {
                    SQLTimeRecorder.RecordSQLResponseTime(CurrentDatabase, ConnectionString, commandText, executionTime.TotalMilliseconds);
                }
            }
            catch (Exception ex)
            {
                ProcessException(ex);
            }
            finally
            {
                dh?.Dispose();
            }
        }
    
    	#endregion
		
		#region Tables Methods
		
		public ExecutionResult<List<TableColumnMetadata>> GetTablesAndColumnsMetadata()
		{
		    return GetTablesAndColumnsMetadata(ConnectionState.CloseOnExit);
		}
		public ExecutionResult<List<TableColumnMetadata>> GetTablesAndColumnsMetadata(ConnectionState connectionState)
		{
		    if (CurrentConnection is null)
		        throw new NullReferenceException(nameof(CurrentConnection));
		
		    if (CurrentCommand is null)
		        throw new NullReferenceException(nameof(CurrentCommand));
		
		    var executionResult = new ExecutionResult<List<TableColumnMetadata>>();
		    var metadataList = new List<TableColumnMetadata>();
		
		    try
		    {
		        var tableNames = GetTables(connectionState);
		
		        foreach (var tableName in tableNames)
		        {
		            metadataList.AddRange(GetColumns(tableName, connectionState));
		        }
		        executionResult.WasSuccessful = true;
		        executionResult.Result = metadataList;
		
		    }
		    catch (Exception ex)
		    {
		        executionResult.WasSuccessful = false;
		        ProcessException(ex);
		    }
		    finally
		    {
		        LogSQLStatementsIfNecessary(CurrentCommand);
		        
		        CurrentCommand.Parameters.Clear();
		        
		    }
		    return executionResult;
		}
		
		private List<string> GetTables(ConnectionState connectionState)
		{
		    if (CurrentConnection is null)
		        throw new NullReferenceException(nameof(CurrentConnection));
		
		    if (CurrentCommand is null)
		        throw new NullReferenceException(nameof(CurrentCommand));
		
		    var tableNames = new List<string>();
		
		    Type connectionType = CurrentConnection.GetType();
		    string? _providerName = connectionType.Namespace;
		    //.var configurationInfo = ConfigurationHelper.GetInfo();
		    //.string _providerName = configurationInfo.ProviderName;
		
		    if (!TransactionHelper.IsInTransactionMode && CurrentConnection.State is System.Data.ConnectionState.Closed)
		    {
		        CurrentConnection.Open();
		    }
		    try
		    {
		        var schemaTables = CurrentConnection.GetSchema("Tables");
		        foreach (DataRow row in schemaTables.Rows)
		        {
		            string tableName = row["TABLE_NAME"].ToString();
		            tableNames.Add(tableName);
		        }
		    }
		    catch
		    {
		        string query = _providerName switch
		        {
		            PROVIDER_SQL_SERVER_CLIENT => "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE';",
		            PROVIDER_POSTGRES_CLIENT => "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public';",
		            PROVIDER_ORACLE_CLIENT => "SELECT table_name FROM user_tables;",
		            PROVIDER_SQLITE_CLIENT => "SELECT name FROM sqlite_master WHERE type='table';",
		            "Microsoft.Data.Sqlite" => "SELECT name FROM sqlite_master WHERE type='table';",
		            PROVIDER_ODBC_CLIENT => "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES;",
		            PROVIDER_OLEDB_CLIENT => "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='TABLE';",
		            _ => throw new NotSupportedException("The provider is not supported.")
		        };
		
		        CurrentCommand.CommandText = query;
		
		        var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
		        if (connectionState is ConnectionState.CloseOnExit)
		        {
		            if (TransactionHelper.IsInTransactionMode)
		                CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
		            else
		                CurrentDataReader = CurrentCommand.ExecuteReader(CommandBehavior.CloseConnection);
		        }
		        else
		        {
		            if (TransactionHelper.IsInTransactionMode)
		                CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
		            else
		                CurrentDataReader = CurrentCommand.ExecuteReader();
		        }
		
		        LastExecutedSQLTime = System.Diagnostics.Stopwatch.GetElapsedTime(startTime);// return the amount of time it took to execute
		        LastExecutedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
				SaveSQLResponseTime(CurrentCommand.CommandText, LastExecutedSQLTime);
		
		        using (var reader = CurrentDataReader)
		        {
		            while (reader.Read())
		            {
		                tableNames.Add(reader.GetString(0));
		            }
		        }
		    }
		    return tableNames;
		}
		
		private List<TableColumnMetadata> GetColumns(string tableName, ConnectionState connectionState)
		{
		    if (CurrentConnection is null)
		        throw new NullReferenceException(nameof(CurrentConnection));
		
		    if (CurrentCommand is null)
		        throw new NullReferenceException(nameof(CurrentCommand));
		
		    var columns = new List<TableColumnMetadata>();
		
		    if (!TransactionHelper.IsInTransactionMode && CurrentConnection.State is System.Data.ConnectionState.Closed)
		    {
		        CurrentConnection.Open();
		    }
		    try
		    {
		        DataTable columnsSchema = CurrentConnection.GetSchema("Columns", new string[] { null, null, tableName });
		        foreach (DataRow row in columnsSchema.Rows)
		        {
		            var columnMetadata = new TableColumnMetadata
		            {
		                TableName = tableName,
		                ColumnName = row["COLUMN_NAME"].ToString(),
		                DataType = row["DATA_TYPE"].ToString(),
		                CharacterMaximumLength = row["CHARACTER_MAXIMUM_LENGTH"] != DBNull.Value ? Convert.ToInt32(row["CHARACTER_MAXIMUM_LENGTH"]) : (int?)null,
		                ColumnDefault = row["COLUMN_DEFAULT"] != DBNull.Value ? row["COLUMN_DEFAULT"].ToString() : null
		            };
		            columns.Add(columnMetadata);
		        }
		    }
		    catch
		    {
		        string query = GetColumnsQuery(tableName);
		        CurrentCommand.CommandText = query;
		        var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
		        if (connectionState is ConnectionState.CloseOnExit)
		        {
		            if (TransactionHelper.IsInTransactionMode)
		                CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
		            else
		                CurrentDataReader = CurrentCommand.ExecuteReader(CommandBehavior.CloseConnection);
		        }
		        else
		        {
		            if (TransactionHelper.IsInTransactionMode)
		                CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
		            else
		                CurrentDataReader = CurrentCommand.ExecuteReader();
		        }
		        
		
		        LastExecutedSQLTime = System.Diagnostics.Stopwatch.GetElapsedTime(startTime);// return the amount of time it took to execute
		        LastExecutedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
				SaveSQLResponseTime(CurrentCommand.CommandText, LastExecutedSQLTime);
		
		        using (var reader = CurrentDataReader)
		        {
		            while (reader.Read())
		            {
		                var columnMetadata = new TableColumnMetadata
		                {
		                    TableName = tableName,
		                    ColumnName = reader.GetString(0),
		                    DataType = reader.GetString(1),
		                    CharacterMaximumLength = reader.IsDBNull(2) ? null : (int?)reader.GetInt32(2),
		                    ColumnDefault = reader.IsDBNull(3) ? null : reader.GetString(3)
		                };
		                columns.Add(columnMetadata);
		            }
		        }
		    }
		    return columns;
		}
		private string GetColumnsQuery(string tableName)
		{
		    if (CurrentConnection is null)
		        throw new NullReferenceException(nameof(CurrentConnection));
		    Type connectionType = CurrentConnection.GetType();
		    string? _providerName = connectionType.Namespace;
		    //.var configurationInfo = ConfigurationHelper.GetInfo();
		    //.string _providerName = configurationInfo.ProviderName;
		
		    return _providerName switch
		    {
		        PROVIDER_SQL_SERVER_CLIENT => $"SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}';",
		        PROVIDER_POSTGRES_CLIENT => $"SELECT column_name, data_type, character_maximum_length, column_default FROM information_schema.columns WHERE table_name = '{tableName}';",
		        PROVIDER_ORACLE_CLIENT => $"SELECT column_name, data_type, data_length AS CHARACTER_MAXIMUM_LENGTH, data_default AS COLUMN_DEFAULT FROM user_tab_columns WHERE table_name = '{tableName.ToUpper()}';",
		        PROVIDER_SQLITE_CLIENT => $"PRAGMA table_info({tableName});",
		        "Microsoft.Data.Sqlite" => $"PRAGMA table_info({tableName});",
		        PROVIDER_ODBC_CLIENT => $"SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}';",
		        PROVIDER_OLEDB_CLIENT => $"SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}';",
		        _ => throw new NotSupportedException("The provider is not supported.")
		    };
		}
		
		#endregion
		
		#region Views Methods

		public ExecutionResult<List<ViewMetadata>> GetViews()
		{
		    return GetViews(ConnectionState.CloseOnExit);
		}
		public ExecutionResult<List<ViewMetadata>> GetViews(ConnectionState connectionState)
		{
		    if (CurrentConnection is null)
		        throw new NullReferenceException(nameof(CurrentConnection));
		
		    if (CurrentCommand is null)
		        throw new NullReferenceException(nameof(CurrentCommand));
		
		    var executionResult = new ExecutionResult<List<ViewMetadata>>();
		    var metadataList = new List<ViewMetadata>();
		
		    Type connectionType = CurrentConnection.GetType();
		    string? _providerName = connectionType.Namespace;
		    //.var configurationInfo = ConfigurationHelper.GetInfo();
		    //.string _providerName = configurationInfo.ProviderName;
		
		    
		    if (!TransactionHelper.IsInTransactionMode && CurrentConnection.State is System.Data.ConnectionState.Closed)
		    {
		        CurrentConnection.Open();
		    }
		
		    try
		    {
		        DataTable viewsSchema = GetViewsSchema(_providerName);
		        foreach (DataRow row in viewsSchema.Rows)
		        {
		            string viewName = row["TABLE_NAME"].ToString();
		            metadataList.Add(new ViewMetadata { ViewName = viewName });
		        }
		        executionResult.WasSuccessful = true;
		        executionResult.Result = metadataList;
		    }
		    catch
		    {
		        string query = GetViewsQuery(_providerName);
		
		        CurrentCommand.CommandText = query;
		        //.CurrentCommand.CommandType = CommandType.Text;
		        //.CurrentCommand.CommandTimeout = CommandTimeOut;
		        
		        try
		        {
		            var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
		            if (connectionState is ConnectionState.CloseOnExit)
		            {
		                if (TransactionHelper.IsInTransactionMode)
		                    CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
		                else
		                    CurrentDataReader = CurrentCommand.ExecuteReader(CommandBehavior.CloseConnection);
		            }
		            else
		            {
		                if (TransactionHelper.IsInTransactionMode)
		                    CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
		                else
		                    CurrentDataReader = CurrentCommand.ExecuteReader();
		            }
		            
		            LastExecutedSQLTime = System.Diagnostics.Stopwatch.GetElapsedTime(startTime);// return the amount of time it took to execute
		            LastExecutedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
					SaveSQLResponseTime(CurrentCommand.CommandText, LastExecutedSQLTime);
		
		            while (CurrentDataReader.Read())
		            {
		                var viewMetadata = new ViewMetadata
		                {
		                    ViewName = CurrentDataReader.GetString(0),
		
		                };
		                metadataList.Add(viewMetadata);
		            }
		            executionResult.WasSuccessful = true;
		            executionResult.Result = metadataList;
		
		            
		        }
		        catch (Exception ex)
		        {
		            executionResult.WasSuccessful = false;
		            ProcessException(ex);
		        }
		        
		    }
		    finally
		    {
		        LogSQLStatementsIfNecessary(CurrentCommand);
		
		        CurrentCommand.Parameters.Clear();
		    }
		
		
		
		    return executionResult;
		}
		private DataTable GetViewsSchema(string providerName)
		{
		    if (CurrentConnection is null)
		        throw new NullReferenceException(nameof(CurrentConnection));
		
		    if (providerName == PROVIDER_SQL_SERVER_CLIENT || providerName == PROVIDER_ODBC_CLIENT || providerName == PROVIDER_OLEDB_CLIENT)
		    {
		        return CurrentConnection.GetSchema("Views");
		    }
		    else if (providerName == PROVIDER_POSTGRES_CLIENT)
		    {
		        return CurrentConnection.GetSchema("Tables", new string[] { null, "public", null, "VIEW" });
		    }
		    else if (providerName == PROVIDER_ORACLE_CLIENT)
		    {
		        return CurrentConnection.GetSchema("Views");
		    }
		    else
		    {
		        throw new NotSupportedException("The provider is not supported for GetSchema views.");
		    }
		}
		private string GetViewsQuery(string _providerName)
		{
		    return _providerName switch
		    {
		        PROVIDER_SQL_SERVER_CLIENT =>
		            "SELECT TABLE_NAME, VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS;",
		        PROVIDER_POSTGRES_CLIENT =>
		            "SELECT table_name, view_definition FROM information_schema.views WHERE table_schema = 'public';",
		        PROVIDER_ORACLE_CLIENT =>
		            "SELECT view_name, text FROM user_views;",
		        PROVIDER_SQLITE_CLIENT =>
		            "SELECT name AS view_name, sql AS view_definition FROM sqlite_master WHERE type='view';",
		        "Microsoft.Data.Sqlite" =>
		            "SELECT name AS view_name, sql AS view_definition FROM sqlite_master WHERE type='view';",
		        PROVIDER_ODBC_CLIENT =>
		            "SELECT TABLE_NAME, VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS;",
		        PROVIDER_OLEDB_CLIENT =>
		            "SELECT TABLE_NAME, VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS;",
		        _ => throw new NotSupportedException("The provider is not supported.")
		    };
		}
		#endregion
		
		#region Triggers Methods

		public ExecutionResult<List<TriggerMetadata>> GetTriggersMetadata()
		{
		    return GetTriggersMetadata(ConnectionState.CloseOnExit);
		}
		public ExecutionResult<List<TriggerMetadata>> GetTriggersMetadata(ConnectionState connectionState)
		{
		    if (CurrentConnection is null)
		        throw new NullReferenceException(nameof(CurrentConnection));
		
		    if (CurrentCommand is null)
		        throw new NullReferenceException(nameof(CurrentCommand));
		
		    var executionResult = new ExecutionResult<List<TriggerMetadata>>();
		    var metadataList = new List<TriggerMetadata>();
		
		    if (!TransactionHelper.IsInTransactionMode && CurrentConnection.State is System.Data.ConnectionState.Closed)
		    {
		        CurrentConnection.Open();
		    }
		
		    try
		    {
		        DataTable? triggersSchema = null;
		        // Providers such as SQL Server may not directly support fetching trigger metadata using GetSchema.
		        triggersSchema = CurrentConnection.GetSchema("Triggers");
		
		        if (triggersSchema != null)
		        {
		            foreach (DataRow row in triggersSchema.Rows)
		            {
		                metadataList.Add(new TriggerMetadata
		                {
		                    TriggerName = row["TRIGGER_NAME"].ToString(),
		                    TriggerType = row["ACTION_TIMING"]?.ToString()
		                });
		            }
		        }
		        else
		        {
		            // with query
		        }
		        executionResult.WasSuccessful = true;
		        executionResult.Result = metadataList;
		    }
		    catch
		    {
		        string query = GetTriggersQuery();
		
		        CurrentCommand.CommandText = query;
		        CurrentCommand.CommandType = CommandType.Text;
		        CurrentCommand.CommandTimeout = CommandTimeOut;
		
		        try
		        {
		            if (connectionState is ConnectionState.CloseOnExit)
		            {
		                if (TransactionHelper.IsInTransactionMode)
		                    CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
		                else
		                    CurrentDataReader = CurrentCommand.ExecuteReader(CommandBehavior.CloseConnection);
		            }
		            else
		            {
		                if (TransactionHelper.IsInTransactionMode)
		                    CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
		                else
		                    CurrentDataReader = CurrentCommand.ExecuteReader();
		            }
		
		            LastExecutedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
		
		            while (CurrentDataReader.Read())
		            {
		                var triggerMetadata = new TriggerMetadata
		                {
		                    TriggerName = CurrentDataReader.GetString(0),
		                    TriggerType = CurrentDataReader.IsDBNull(1) ? null : CurrentDataReader.GetString(1),
		                };
		                metadataList.Add(triggerMetadata);
		            }
		            executionResult.WasSuccessful = true;
		            executionResult.Result = metadataList;
		        }
		        catch (Exception ex)
		        {
		            executionResult.WasSuccessful = false;
		            ProcessException(ex);
		        }
		
		    }
		
		    finally
		    {
		        LogSQLStatementsIfNecessary(CurrentCommand);
		
		        CurrentCommand.Parameters.Clear();
		    }
		
		    return executionResult;
		}
		
		private string GetTriggersQuery()
		{
		    if (CurrentConnection is null)
		        throw new NullReferenceException(nameof(CurrentConnection));
		
		    Type connectionType = CurrentConnection.GetType();
		    string? _providerName = connectionType.Namespace;
		    //.var configurationInfo = ConfigurationHelper.GetInfo();
		    //.string _providerName = configurationInfo.ProviderName;
		
		    return _providerName switch
		    {
		        PROVIDER_SQL_SERVER_CLIENT =>
		            "SELECT t.name AS TriggerName, s.name AS SchemaName, p.name AS TableName, m.definition AS TriggerDefinition " +
		            "FROM sys.triggers t " +
		            "JOIN sys.objects p ON t.parent_id = p.object_id " +
		            "JOIN sys.schemas s ON p.schema_id = s.schema_id " +
		            "JOIN sys.sql_modules m ON t.object_id = m.object_id;",
		        PROVIDER_POSTGRES_CLIENT =>
		            "SELECT tgname, (CASE WHEN tgtype & 2 <> 0 THEN 'BEFORE' ELSE 'AFTER' END) AS event_time, pg_catalog.pg_get_triggerdef(t.oid) FROM pg_trigger t JOIN pg_class c ON t.tgrelid = c.oid WHERE NOT tgisinternal;",
		        PROVIDER_ORACLE_CLIENT =>
		            "SELECT trigger_name, triggering_event, trigger_type, trigger_body FROM user_triggers;",
		        PROVIDER_SQLITE_CLIENT =>
		            "SELECT name, type, sql FROM sqlite_master WHERE type = 'trigger';",
		        "Microsoft.Data.Sqlite" =>
		            "SELECT name, type, sql FROM sqlite_master WHERE type = 'trigger';",
		        PROVIDER_ODBC_CLIENT =>
		            // Generic query if applicable: adjust if connecting to specific database types.
		            "SELECT TRIGGER_NAME, EVENT_OBJECT_TABLE, ACTION_STATEMENT FROM INFORMATION_SCHEMA.TRIGGERS;",
		        PROVIDER_OLEDB_CLIENT =>
		            // Similar to ODBC: may require specific adjustments depending on the connected system.
		            "SELECT TRIGGER_NAME, EVENT_OBJECT_TABLE, ACTION_STATEMENT FROM INFORMATION_SCHEMA.TRIGGERS;",
		        _ => throw new NotSupportedException("The provider is not supported.")
		    };
		}
		#endregion
		
		#region StoredProcedure Methods
		public ExecutionResult<List<StoredProcedureMetadata>> GetStoredProceduresMetadata()
		{
		    return GetStoredProceduresMetadata(ConnectionState.CloseOnExit);
		}
		public ExecutionResult<List<StoredProcedureMetadata>> GetStoredProceduresMetadata(ConnectionState connectionState)
		{
		    if (CurrentConnection is null)
		        throw new NullReferenceException(nameof(CurrentConnection));
		
		    if (CurrentCommand is null)
		        throw new NullReferenceException(nameof(CurrentCommand));
		
		    var executionResult = new ExecutionResult<List<StoredProcedureMetadata>>();
		    var metadataList = new List<StoredProcedureMetadata>();
		
		    // SQLite doesn't support stored procedures, but you can retrieve user-defined functions if any
		
		    if (!TransactionHelper.IsInTransactionMode && CurrentConnection.State is System.Data.ConnectionState.Closed)
		    {
		        CurrentConnection.Open();
		    }
		    try
		    {
		        DataTable? proceduresSchema = null;
		        proceduresSchema = CurrentConnection.GetSchema("Procedures");
		
		        if (proceduresSchema != null)
		        {
		            foreach (DataRow row in proceduresSchema.Rows)
		            {
		                metadataList.Add(new StoredProcedureMetadata
		                {
		                    ProcedureName = row["ROUTINE_NAME"].ToString(),
		                });
		            }
		            executionResult.WasSuccessful = true;
		            executionResult.Result = metadataList;
		        }
		        else
		        { 
		            // with query
		        }
		    }
		    catch
		    {
		        try
		        {
		            string query = GetStoredProcedureQuery();
		
		            CurrentCommand.CommandText = query;
		            CurrentCommand.CommandType = CommandType.Text;
		            CurrentCommand.CommandTimeout = CommandTimeOut;
		
		            var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
		            if (connectionState is ConnectionState.CloseOnExit)
		            {
		                if (TransactionHelper.IsInTransactionMode)
		                    CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
		                else
		                    CurrentDataReader = CurrentCommand.ExecuteReader(CommandBehavior.CloseConnection);
		            }
		            else
		            {
		                if (TransactionHelper.IsInTransactionMode)
		                    CurrentDataReader = TransactionHelper.ExecuteReader((DatabaseHelper)this.MemberwiseClone());
		                else
		                    CurrentDataReader = CurrentCommand.ExecuteReader();
		            }
		
		            LastExecutedSQLTime = System.Diagnostics.Stopwatch.GetElapsedTime(startTime);// return the amount of time it took to execute
		            LastExecutedSQL = SQLLoggingHelper.DatabaseCommandAsTSQL(CurrentCommand);
					SaveSQLResponseTime(CurrentCommand.CommandText, LastExecutedSQLTime);
		
		            while (CurrentDataReader.Read())
		            {
		                var procedureMetadata = new StoredProcedureMetadata
		                {
		                    ProcedureName = CurrentDataReader.GetString(0),
		                };
		                metadataList.Add(procedureMetadata);
		            }
		            executionResult.WasSuccessful = true;
		            executionResult.Result = metadataList;
		        }
		        catch (Exception ex)
		        {
		            executionResult.WasSuccessful = false;
		            ProcessException(ex);
		        }
		    }        
		
		    finally
		    {
		        LogSQLStatementsIfNecessary(CurrentCommand);
		
		        CurrentCommand.Parameters.Clear();
		    }
		
		    return executionResult;
		}
		
		private string GetStoredProcedureQuery()
		{
		    if (CurrentConnection is null)
		        throw new NullReferenceException(nameof(CurrentConnection));
		
		    Type connectionType = CurrentConnection.GetType();
		    string? _providerName = connectionType.Namespace;
		    //.var configurationInfo = ConfigurationHelper.GetInfo();
		    //.string _providerName = configurationInfo.ProviderName;
		
		    return _providerName switch
		    {
		        PROVIDER_SQL_SERVER_CLIENT =>
		            "SELECT SPECIFIC_NAME, ROUTINE_DEFINITION FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE='PROCEDURE';",
		        PROVIDER_POSTGRES_CLIENT =>
		            "SELECT proname, prosrc FROM pg_proc p JOIN pg_namespace n ON p.pronamespace = n.oid WHERE n.nspname = 'public';",
		        PROVIDER_ORACLE_CLIENT =>
		            "SELECT OBJECT_NAME, OBJECT_TYPE FROM USER_OBJECTS WHERE OBJECT_TYPE = 'PROCEDURE';",
		        PROVIDER_SQLITE_CLIENT =>
		            // SQLite doesn't support stored procedures, but you can retrieve user-defined functions if any
		            "SELECT name FROM sqlite_master WHERE type='table' AND name='sqlite_functions';",
		        "Microsoft.Data.Sqlite" =>
		            // SQLite doesn't support stored procedures, but you can retrieve user-defined functions if any
		            "SELECT name FROM sqlite_master WHERE type='table' AND name='sqlite_functions';",
		        PROVIDER_ODBC_CLIENT =>
		            // Assuming ODBC can connect to a system with stored procedure support; use INFORMATION_SCHEMA
		            "SELECT SPECIFIC_NAME, ROUTINE_DEFINITION FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE='PROCEDURE';",
		        PROVIDER_OLEDB_CLIENT =>
		            // Similar approach as for ODBC since it depends on the connected database
		            "SELECT SPECIFIC_NAME, ROUTINE_DEFINITION FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE='PROCEDURE';",
		        _ => throw new NotSupportedException("The provider is not supported.")
		    };
		}
		#endregion
    
    #region Utility Methods
    private static DbParameter CloneParameter(DbParameter originalParameter, DbCommand targetCommand)
    {
        var clone = targetCommand.CreateParameter();

        clone.ParameterName = originalParameter.ParameterName;
        clone.DbType = originalParameter.DbType;
        clone.Value = originalParameter.Value;
        clone.Direction = originalParameter.Direction;
        clone.Size = originalParameter.Size;
        clone.Precision = originalParameter.Precision;
        clone.Scale = originalParameter.Scale;
        clone.IsNullable = originalParameter.IsNullable;
        clone.SourceColumn = originalParameter.SourceColumn;
        clone.SourceVersion = originalParameter.SourceVersion;

        return clone;
    }
    #endregion

    }
    
    #region Internal Classes
    #region SQLLoggingHelper
    /// <summary>
    /// This class provides the functionality to log sql statements and stored procedure
    /// calls to a text for debugging and logging purposes.
    /// </summary>
    internal static class SQLLoggingHelper
    {
    
        public enum LoggingStyle
        {
            Compact = 0,
            Detailed = 1,
            Both = 2
        }
    
        public static string SQL_Compact_Log_FileName = "SQL Compact Log File.txt";
        public static string SQL_Detail_Log_FileName = "SQL Detail Log File.txt";
    
        private static SingletonLogWriter _singletonLogWriter = SingletonLogWriter.Instance;
    
    
        /// <summary>
        /// This method will take a Database parameter and convert it to a proper
        /// value for a SQL statement.
        /// </summary>
        ///
        /// <param name="dp" type = "DbParameter">The generic database parameter to convert.</param>
        ///
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author			      	  	Date		            Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        public static string ParameterValueFormattedForSQLStatement(DbParameter dp)
        {
            if (dp.Value == null || dp.Value == DBNull.Value)
                return "NULL";

            object value = dp.Value;
            switch (dp.DbType)
            {
                case DbType.AnsiString:
                case DbType.AnsiStringFixedLength:
                case DbType.String:
                case DbType.StringFixedLength:
                case DbType.Guid:
                case DbType.Time:
                case DbType.Xml:
                case DbType.Date:
                case DbType.DateTime:
                case DbType.DateTime2:
                case DbType.DateTimeOffset:
                    {
                        // Use fast path for string escaping
                        string str = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
                        if (str.Length == 0) return "''";
                        return $"'{str.Replace("'", "''")}'";
                    }

                case DbType.Boolean:
                    {
                        if (value is bool b)
                            return b ? "1" : "0";

                        // Handle numeric / string values gracefully
                        if (value is IConvertible c)
                        {
                            try
                            {
                                return c.ToBoolean(System.Globalization.CultureInfo.InvariantCulture) ? "1" : "0";
                            }
                            catch
                            {
                                // fallback: parse int or text
                                var s = value.ToString();
                                if (int.TryParse(s, out int n))
                                    return n != 0 ? "1" : "0";
                            }
                        }
                        return "0";
                    }

                case DbType.Byte:
                case DbType.SByte:
                case DbType.Int16:
                case DbType.Int32:
                case DbType.Int64:
                case DbType.UInt16:
                case DbType.UInt32:
                case DbType.UInt64:
                case DbType.Single:
                case DbType.Double:
                case DbType.Decimal:
                    {
                        if (value is IFormattable f)
                            return f.ToString(null, System.Globalization.CultureInfo.InvariantCulture)!;
                        return value.ToString()!;
                    }

                default:
                    {
                        string s = value.ToString()!;
                        return $"'{s.Replace("'", "''")}'";
                    }
            }
        }
    
        /// <summary>
        /// This method will take a DbCommand object format and log it based upon
        /// it's command text, parameter names, and parameter values.
        /// </summary>
        ///
        /// <param name="cmd" type = "DbCommand">The DbCommand to format and log.</param>
        ///
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author			      	  	Date		            Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        public static void LogSQLStatement(DbCommand cmd, LoggingStyle style)
        {
    
            if (cmd.CommandText is not null && cmd.CommandText.Length > 0)
            {
    
                if (style is LoggingStyle.Detailed)
                {
                    LogDetailedSQLStatement(cmd, SQL_Detail_Log_FileName);
                }
                else if (style is LoggingStyle.Compact)
                {
                    LogCompactSQLStatement(cmd, SQL_Compact_Log_FileName);
                }
                else
                {
                    LogDetailedSQLStatement(cmd, SQL_Detail_Log_FileName);
                    LogCompactSQLStatement(cmd, SQL_Compact_Log_FileName);
                }
            }
        }
    
        private static void LogCompactSQLStatement(DbCommand cmd, string compactFileName)
        {
            var textToRecord = cmd.CommandText;
    
            // determine if if the command text is standard in-line SQL with parameters (ex: Select * from Employee where lastname = '@lastname'
            // and if so we replace those with the actual value.  if this is not embedded sql with parameters then it must be a stored proc
            // so concat those parameters and values separately
    
            if (textToRecord.Contains("@"))
            {
                foreach (DbParameter p in cmd.Parameters)
                {
                    textToRecord = textToRecord.Replace(p.ParameterName, ParameterValueFormattedForSQLStatement(p));
                }
            }
            else
            {
                foreach (DbParameter p in cmd.Parameters)
                {
                    textToRecord += $" {p.ParameterName} = {ParameterValueFormattedForSQLStatement(p)},";
                }
    
                textToRecord = textToRecord.Remove(textToRecord.Length - 1);  //remove last comma
    
            }
    
            // Record to a local file.            
            _singletonLogWriter.WriteText(textToRecord + System.Environment.NewLine, compactFileName);
        }
    
        private static void LogDetailedSQLStatement(DbCommand cmd, string detailedFileName)
        {
    
            var msg = new StringBuilder();
            var textToRecord = string.Empty;
            var maxLengthOfaLineOfText = 0;
    
            textToRecord = string.Format("\r\nRecorded @:\t{0}", DateTime.Now.ToString("MMMM dd, yyyy hh:mm:ss tt"));
            maxLengthOfaLineOfText = (textToRecord.Length > maxLengthOfaLineOfText) ? textToRecord.Length : maxLengthOfaLineOfText;
            msg.AppendLine(textToRecord);
    
            textToRecord = string.Format("Database:\t{0}", cmd.Connection!.Database);
            maxLengthOfaLineOfText = (textToRecord.Length > maxLengthOfaLineOfText) ? textToRecord.Length : maxLengthOfaLineOfText;
            msg.AppendLine(textToRecord);
    
            textToRecord = string.Format("Statement:\t{0}", cmd.CommandText);
            maxLengthOfaLineOfText = (textToRecord.Length > maxLengthOfaLineOfText) ? textToRecord.Length : maxLengthOfaLineOfText;
            msg.AppendLine(textToRecord);
            msg.AppendLine("Paramters:");
    
            foreach (DbParameter p in cmd.Parameters)
            {
                textToRecord = string.Format("\t\t{0}: {1}", p.ParameterName, p.Value!.ToString());
                msg.AppendLine(textToRecord);
                maxLengthOfaLineOfText = (textToRecord.Length > maxLengthOfaLineOfText) ? textToRecord.Length : maxLengthOfaLineOfText;
            }
    
            // We have built the contents of the actual message so lets letter box it
            msg.Insert(0, new string('*', maxLengthOfaLineOfText + 10));
            msg.AppendLine(new string('*', maxLengthOfaLineOfText + 10));
    
            // Record to a local file.
            _singletonLogWriter.WriteText(textToRecord + System.Environment.NewLine, detailedFileName);
    
            msg.Clear();
        }
    
        public static void LogSQLStatement(DbCommand cmd)
        {
            LogSQLStatement(cmd, LoggingStyle.Detailed);
        }
    
        public static string DatabaseCommandAsTSQL(DbCommand cmd, bool includeUseDatabaseName = true, bool includeParameters = true)
        {
            if (cmd is null) throw new ArgumentNullException(nameof(cmd));

            var sql = new StringBuilder(256);
            var parameters = cmd.Parameters;
            var paramCount = parameters.Count;

            if (includeUseDatabaseName && cmd.Connection != null)
                sql.Append("USE ").Append(cmd.Connection.Database).AppendLine(";");

            switch (cmd.CommandType)
            {
                case CommandType.StoredProcedure:
                    {
                        sql.AppendLine("DECLARE @return_value INT;");

                        for (int i = 0; i < paramCount; i++)
                        {
                            var dp = parameters[i];
                            if (dp.Direction == ParameterDirection.Output || dp.Direction == ParameterDirection.InputOutput)
                            {
                                sql.Append("DECLARE ")
                                   .Append(dp.ParameterName)
                                   .Append('\t')
                                   .Append(dp.DbType)
                                   .Append("\t= ")
                                   .AppendLine(dp.Direction == ParameterDirection.Output
                                       ? "NULL;"
                                       : ParameterValueFormattedForSQLStatement(dp) + ";");
                            }
                        }

                        sql.Append("EXEC [").Append(cmd.CommandText).AppendLine("]");
                        bool isFirstParam = true;

                        for (int i = 0; i < paramCount; i++)
                        {
                            var dp = parameters[i];
                            if (dp.Direction == ParameterDirection.ReturnValue) continue;

                            sql.Append(isFirstParam ? "\t" : "\t, ");
                            isFirstParam = false;

                            if (dp.Direction == ParameterDirection.Input)
                                sql.Append(dp.ParameterName).Append(" = ").AppendLine(ParameterValueFormattedForSQLStatement(dp));
                            else
                                sql.Append(dp.ParameterName).Append(" = ").Append(dp.ParameterName).AppendLine(" OUTPUT");
                        }

                        sql.AppendLine(";")
                           .AppendLine("SELECT 'Return Value' = CONVERT(VARCHAR, @return_value);");

                        for (int i = 0; i < paramCount; i++)
                        {
                            if (parameters[i] is SqlParameter sp &&
                                (sp.Direction == ParameterDirection.Output || sp.Direction == ParameterDirection.InputOutput))
                            {
                                sql.Append("SELECT '").Append(sp.ParameterName)
                                   .Append("' = CONVERT(VARCHAR, ")
                                   .Append(sp.ParameterName)
                                   .AppendLine(");");
                            }
                        }

                        break;
                    }

                case CommandType.Text:
                    {
                        var formattedTSQL = cmd.CommandText;

                        if (paramCount > 0 && includeParameters)
                        {
                            var textBuilder = new StringBuilder(formattedTSQL.Length + 128);
                            textBuilder.Append(formattedTSQL);

                            for (int i = 0; i < paramCount; i++)
                            {
                                var dp = parameters[i];
                                textBuilder.Replace(dp.ParameterName, ParameterValueFormattedForSQLStatement(dp));
                            }

                            sql.AppendLine(textBuilder.ToString()).Append(';');
                        }
                        else
                        {
                            sql.AppendLine(formattedTSQL);

                            if (!formattedTSQL.AsSpan().TrimEnd().EndsWith(";"))
                                sql.Append(';');
                        }

                        break;
                    }
            }

            return sql.ToString();
        }
    }
    #endregion

		#region Configuration Helper
    internal static class ConfigurationHelper
    {
        private static ConfigurationInfo _configurationInfo = new();
    
        public static string ConfigFileUsed { get; private set; } = string.Empty;
    
        internal struct ConfigurationInfo
				{
				    public string ProviderName;
				    public string ConnectionString;
				    public string? BackupConnectionString;
				    public bool ShouldUseBackupServer;
				}
    
    	public static bool IsConnectionStringLoaded()
			{
			    return _configurationInfo.ConnectionString is not null && _configurationInfo.ConnectionString.Length > 0;
			}
    
      public static ConfigurationInfo GetInfo(string configFileAndPath = "")
			{

			    if (_configurationInfo.ConnectionString is not null)
			    {
			        // if the connection information has already been retrieved
			        // then return it so we don't have to read the config file 
			        // again.
			        return _configurationInfo;
			    }
			    else
			    {
			        // Our static variable is empty so we need to find our config
			        // file (or use what was passed in) and load the appropriate 
			        // connection string and provider.  For web apps, the config file
			        // could exist as the 'web.config' but for windows apps, it would 
			        // app.config where 'app' is replaced with the name of the actual
			        // application executable name.


			        //Use the specified config file if it was passed in.
			        if (File.Exists(configFileAndPath))
			        {
			            ConfigFileUsed = configFileAndPath;

			            configFileAndPath = configFileAndPath.Trim();

			            if (configFileAndPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			            {
			                var configurationBuilder = new ConfigurationBuilder();
			                configurationBuilder.AddJsonFile(configFileAndPath, true);

			                _configurationInfo = LoadFromIConfiguration(configurationBuilder.Build());
			                return _configurationInfo;
			            }
			            else
			            {
			                _configurationInfo = LoadFromXmlFile(configFileAndPath);
			                return _configurationInfo;
			            }
			        }
			        //Otherwise, look for the config file in supported locations.
			        else
			        {
			            //First, look for custom DLG config file and use that if it exists.
			            //Example: DLG.YourAppName.DataLayer.config
			            //This approach allows dlg to support multiple data layers in the same app using different config files.
			            configFileAndPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "bin", "Inquiry.Benchmarks.DLG.config");

			            if (!File.Exists(configFileAndPath))
			            {
			                configFileAndPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Inquiry.Benchmarks.DLG.config");
			            }

			            if (File.Exists(configFileAndPath))
			            {
			                _configurationInfo = LoadFromXmlFile(configFileAndPath);
			                return _configurationInfo;
			            }

			            //Next, look for the appsettings.*.json files if they exist.
			            var appSettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
			            var devSettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.Development.json");
			            var prodSettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.Production.json");

			            var configurationBuilder = new ConfigurationBuilder();
			            bool isJsonFile = false;
			            if (File.Exists(appSettingsFile))
			            {
			                isJsonFile = true;
			                configurationBuilder.AddJsonFile(appSettingsFile, true);
			            }
			            if (File.Exists(devSettingsFile))
			            {
			                isJsonFile = true;
			                configurationBuilder.AddJsonFile(devSettingsFile, true);
			            }
			            if (File.Exists(prodSettingsFile))
			            {
			                isJsonFile = true;
			                configurationBuilder.AddJsonFile(prodSettingsFile, true);
			            }

			            if (isJsonFile)
			            {
			                ConfigFileUsed = File.Exists(prodSettingsFile) ? prodSettingsFile : File.Exists(devSettingsFile) ? devSettingsFile : appSettingsFile;

			                _configurationInfo = LoadFromIConfiguration(configurationBuilder.Build());
			                return _configurationInfo;
			            }

			            // If the file still does not exists, then we might be dealing with a web application
			            // which will store it's settings in the web.config file.
			            configFileAndPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web.config");

			            // If the file still cannot be found, then it could be using it's own app.config file.  
			            // The 'app' portion of the name will need to be changed to match the name of the application.
			            if (!File.Exists(configFileAndPath))
			                configFileAndPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Assembly.GetEntryAssembly()?.GetName().Name + ".exe.config");

			            // If the file still cannot be found, then it could be using it's own app.config file.  
			            // The 'app' portion of the name will need to be changed to match the name of the application.
			            if (!File.Exists(configFileAndPath))
			                configFileAndPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Assembly.GetEntryAssembly()?.GetName().Name + ".config");


			            if (configFileAndPath.Trim().Length > 0)
			            {
			                _configurationInfo = LoadFromXmlFile(configFileAndPath);
			                return _configurationInfo;
			            }

			            throw new FileNotFoundException($"Unable to determine the configuration file to use. Please use a supported DLG configuration option placed in {AppDomain.CurrentDomain.BaseDirectory}.");
			        }
			    }
			}
    
			private static ConfigurationInfo LoadFromIConfiguration(IConfiguration configuration)
			{
			    var configurationInfo = new ConfigurationInfo();

			    var dlgSection = configuration.GetRequiredSection("DLG");
			    var dataLayerSection = dlgSection.GetRequiredSection("Inquiry.Benchmarks.DLG"); //This should match the namespace of the datalayer

			    var keyToUse = dataLayerSection.GetRequiredSection("ConnectionStringToUse")?.Value ?? throw new Exception("ConnectionStringToUse must be populated.");
			    var backupKeyToUse = dataLayerSection.GetSection("BackupConnectionStringToUse")?.Value ?? string.Empty;

			    var connectionString = configuration.GetConnectionString(keyToUse) ?? throw new Exception($"ConnectionString {keyToUse} must be populated.");

			    var builder = new DbConnectionStringBuilder
			    {
			        ConnectionString = connectionString
			    };

			    configurationInfo.ConnectionString = builder.ConnectionString;
			    configurationInfo.BackupConnectionString = configuration.GetConnectionString(backupKeyToUse) ?? string.Empty;

			    if (!builder.TryGetValue("Provider", out var provider) || provider is null || provider.ToString() is null)
			    {
			        provider = "Microsoft.Data.SqlClient"; // default to sql client if not specified
			    }
			    configurationInfo.ProviderName = provider.ToString()!;

			    configurationInfo.ShouldUseBackupServer = dataLayerSection.GetSection("ShouldUseBackupServer")?.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

			    if (configurationInfo.ShouldUseBackupServer && (configurationInfo.BackupConnectionString is null || configurationInfo.BackupConnectionString.Length == 0))
			    {
			        throw new Exception("BackupConnectionString must be populated when ShouldUseBackupServer is true.");
			    }

			    return configurationInfo;
			}

			private static ConfigurationInfo LoadFromXmlFile(string configFileAndPath)
			{
			    var configurationInfo = new ConfigurationInfo();
			    var fileMap = new ExeConfigurationFileMap
			    {
			        ExeConfigFilename = configFileAndPath
			    };
			    var configFile = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);

			    var keyToUse = configFile.AppSettings.Settings["ConnectionStringToUse"]?.Value ?? throw new Exception("ConnectionStringToUse must be populated.");
			    var backupKeyToUse = configFile.AppSettings.Settings["BackupConnectionStringToUse"]?.Value ?? string.Empty;

			    configurationInfo.ConnectionString = configFile.ConnectionStrings.ConnectionStrings[keyToUse]?.ConnectionString ?? throw new Exception($"ConnectionString {keyToUse} must be populated.");
			    configurationInfo.ProviderName = configFile.ConnectionStrings.ConnectionStrings[keyToUse]?.ProviderName ?? throw new Exception($"Unable to infer provider from connection string {keyToUse}. Please ensure the connection string contains a 'Provider' key.");

			    configurationInfo.BackupConnectionString = configFile.ConnectionStrings.ConnectionStrings[backupKeyToUse]?.ConnectionString ?? string.Empty;

			    configurationInfo.ShouldUseBackupServer = configFile.AppSettings.Settings["ShouldUseBackupServer"]?.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

			    if (configurationInfo.ShouldUseBackupServer && (configurationInfo.BackupConnectionString is null || configurationInfo.BackupConnectionString.Length == 0))
			    {
			        throw new Exception("BackupConnectionString must be populated when ShouldUseBackupServer is true.");
			    }

			    return configurationInfo;
			}
    
    }
    #endregion
    
    
    #region SingletonLogWriter
    /// <summary>
    /// A Logging class implementing the Singleton and Producer/Consumer design patterns.
    /// </summary>
    internal class SingletonLogWriter
    {
    
        struct LogItem
        {
						public bool ShouldWriteTimeStamp;
            public DateTime DateAndTime;
            public string TextToWrite;
            public string FileNameAndPath;
        }
    
        // This approach ensures that only one instance is created and only when the instance is needed.  
        // Also, the variable is declared to be volatile to ensure that assignment to the instance variable completes before the 
        // instance variable can be accessed.  Lastly, this approach uses a syncRoot instance to lock on, rather than locking 
        // on the type itself, to avoid deadlocks.
        private static volatile SingletonLogWriter? instance = null;
        private static object syncRoot = new();
        //private static string _defaultFolderName            = DateTime.Now.ToString("yyyyMMdd");  // ex: 20190115
    
        private readonly ConcurrentQueue<LogItem> _inputMainQueue = new();
        private readonly ConcurrentDictionary<string, ConcurrentQueue<LogItem>> threadDictionary = new();

        private bool isComplete = false;

        // Flush coordination: tracks items still in the pipeline (queued or mid-write) so that
        // Flush() can return the instant everything has reached disk, with no fixed delay.
        private int _pendingItemCount = 0;
        private readonly object _flushSyncRoot = new();
        private readonly System.Threading.ManualResetEventSlim _idleEvent = new(true);
        // Wakes the consumer immediately when work arrives, so there is no fixed polling delay.
        private readonly System.Threading.AutoResetEvent _workSignal = new(false);
    
    
        #region Constructor/Destructor
    
        private SingletonLogWriter()
        {
            // process the queue and write entries from a background thread.
            var consumerTask = new Task(ProcessMainQueue);
            consumerTask.Start();
        }
    
        ~SingletonLogWriter()
        {
            isComplete = true;
        }
    
        #endregion
    
        #region Properties
    
        /// <summary>
        /// The default file name used if a file name is not provided.
        /// </summary>
        public string DefaultLogFileName
        {
            get
            {
                return $"{(System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly()).GetName().Name}.txt";
            }
        }
    
        /// <summary>
        /// The default log file folder where the files will be written.
        /// </summary>
        public string DefaultLogFolderName
        {
            get
            {
                return DateTime.Now.ToString("yyyyMMdd");  // ex: 20190115
            }
        }
    
        public string DefaultLogFolderLocation
        {
            get
            {
                return Path.GetDirectoryName((System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly()).Location) + $"\\Logs\\";
            }
        }
    
        /// <summary>
        /// The default path where the log folder is located.
        /// </summary>
        public string DefaultLoggingFullPath
        {
            get
            {
                var s = $"{DefaultLogFolderLocation}\\{DefaultLogFolderName}\\";
                return s;
            }
        }
        #endregion
    
        #region Methods (Private)
    
        private void ProcessMainQueue()
        {
            while (!isComplete)
            {
                // take each entry on the Queue and write it to a text file.
                while (_inputMainQueue.Count > 0)
                {
                    var li = new LogItem();
                    if (_inputMainQueue.TryDequeue(out li) is true)
                    {
                        var path = li.FileNameAndPath;
    
                        if (threadDictionary.TryGetValue(path, out ConcurrentQueue<LogItem>? queForSpecificFile))
                        {
                            queForSpecificFile.Enqueue(li);
                        }
                        else
                        {
                            // this entry is being written to a new file that is not in our dictionary
                            // which means it has not been defined yet, so add it to its own queue and process on a background thread.
    
                            var newQueue = new ConcurrentQueue<LogItem>();
    
                            newQueue.Enqueue(li);  // add this files entry to be written to disk and create its own processing queue.
    
                            if (threadDictionary.TryAdd(li.FileNameAndPath, newQueue))
                            {
    
                                Task.Factory.StartNew(() => ProcessAQueue(newQueue), TaskCreationOptions.LongRunning);
    
                            }
                            else
                            {
                                // failed to add so throw an exception
                                throw new Exception("Failed to add a log entry to the thread dictionary.");
                            }
                        }
                    }
    
                    //WriteEntriesToDisk();
                    //Console.WriteLine($"Items left to log...{_inputQueue.Count}");
                }
    
                _workSignal.WaitOne(100);  //wake immediately when new work arrives; 100ms safety timeout
            }
    
            //Console.WriteLine("CLEAN UP DONE!");
        }
    
        private void ProcessAQueue(ConcurrentQueue<LogItem> theQ)
        {
            while (!isComplete)
            {
                // take each entry on the Queue and write it to a text file.
                // Write everything queued for this file in a SINGLE open (batched), instead of
                // reopening the file per line. Critical where virus scanners add large latency
                // to each file open.
                if (theQ.Count > 0)
                    WriteQueuedEntriesToDisk(theQ);

                // Accumulation window: items pile up here, then flush together as one batch.
                // Larger = fewer file opens (bigger batches); smaller = lower latency.
                Thread.Sleep(50);
            }
        }
    
        private void WriteQueuedEntriesToDisk(ConcurrentQueue<LogItem> theQ)
        {
            // Drain everything currently queued for this file into a single batch.
            // All entries in a given queue target the same file (queues are keyed by path).
            var batch = new List<LogItem>();
            while (theQ.TryDequeue(out var li))
                batch.Add(li);

            if (batch.Count == 0)
                return;

            try
            {
                var first = batch[0];
                if (first.FileNameAndPath.Length > 0)
                {
                    var directoryName = Path.GetDirectoryName(first.FileNameAndPath);
                    if (directoryName?.Length > 0)
                        Directory.CreateDirectory(directoryName);  // create this directory if needed

                    using (var fileout = File.AppendText(first.FileNameAndPath))  // ONE open for the whole batch
                    {
                        foreach (var li in batch)
                        {
                            if (li.ShouldWriteTimeStamp)
                                fileout.WriteLine($"{li.DateAndTime:MM/dd/yyyy hh:mm:ss.fff tt}:[{Environment.CurrentManagedThreadId}]\t{li.TextToWrite}");
                            else
                                fileout.WriteLine(li.TextToWrite);
                        }
                    }
                }
                else
                {
                    Directory.CreateDirectory(DefaultLoggingFullPath);

                    using (var fileout = File.AppendText(DefaultLoggingFullPath + DefaultLogFileName))  // ONE open for the whole batch
                    {
                        foreach (var li in batch)
                            fileout.WriteLine($"{li.DateAndTime.ToString("MM/dd/yyyy hh:mm:ss.fff tt")}:[{Thread.CurrentThread.ManagedThreadId}]\t{li.TextToWrite}");
                    }
                }
            }
            finally
            {
                lock (_flushSyncRoot)
                {
                    _pendingItemCount -= batch.Count;
                    if (_pendingItemCount <= 0)
                    {
                        _pendingItemCount = 0;
                        _idleEvent.Set();
                    }
                }
            }
        }
    
        #endregion
    
        #region Methods (Public)
    
        public void WriteText(string data, bool shouldRecordTimestamp = true)
        {
            WriteText(data, string.Empty, shouldRecordTimestamp);
        }
    
        public void WriteText(string data, string fileNameAndPath, bool shouldRecordTimestamp = true)
        {
            var li = new LogItem
            {
            ShouldWriteTimeStamp = shouldRecordTimestamp,
    			DateAndTime = DateTime.Now,
                TextToWrite = data,
                FileNameAndPath = fileNameAndPath
            };

            lock (_flushSyncRoot)
            {
                _pendingItemCount++;
                _idleEvent.Reset();
            }

            _inputMainQueue.Enqueue(li);
            _workSignal.Set();
        }
    
        public void Flush()
        {
            // Returns as soon as the pipeline has drained to disk - effectively instant when idle,
            // instead of always blocking for a fixed delay.
            _idleEvent.Wait();
        }
    
        public static SingletonLogWriter Instance
        {
            get
            {
                if (instance is null)
                {
                    lock (syncRoot)
                    {
                        if (instance is null)
                            instance = new SingletonLogWriter();
                    }
                }
    
                return instance;
            }
        }
    
        #endregion
    
    }
    #endregion
    
    #region TransactionHelper
    
    public static class TransactionHelper
    {
        //private static AsyncLocal<IsolationLevel> _isolationLevel = new();
    
        private static readonly AsyncLocal<DatabaseHelper> _databaseHelper = new();
    
        //private static AsyncLocal<bool> isInTransactionMode = new();
    
        public static IsolationLevel TransactionIsolationLevel
				{
				    get
				    {
				        return ((_databaseHelper.Value is not null && _databaseHelper.Value.CurrentTransaction is not null) ? _databaseHelper.Value.CurrentTransaction.IsolationLevel : default);
				    }
				}

				public static DatabaseHelper? DatabaseHelper => _databaseHelper.Value;

				public static bool IsInTransactionMode
				{
				    get
				    {
				        return (_databaseHelper.Value is not null && _databaseHelper.Value.CurrentTransaction is not null);
				    }
				}
    
        /// <summary>
        /// Marks the beginning of a transaction.
        /// </summary>
        ///
        /// <param name="isolationLevel" type="IsolationLevel">The locking level in which to run this transaction.</param>
        ///
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        public static void BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
				{
				    _databaseHelper.Value = new DatabaseHelper();
				    _databaseHelper.Value.BeginTransaction(isolationLevel);
				}
    
        /// <summary>
        /// Marks the beginning of a transaction.
        /// </summary>
        ///
        /// <param name="isolationLevel" type="IsolationLevel">The locking level in which to run this transaction.</param>
        /// 
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        //public static async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
				//{
				//    DatabaseHelper = new DatabaseHelper();
				//    await DatabaseHelper.BeginTransactionAsync(isolationLevel);
				//}
    
        /// <summary>
        /// Commits a transaction.
        /// </summary>
        ///
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        public static void CommitTransaction()
				{
				    _databaseHelper.Value?.CommitTransaction();

				    _databaseHelper.Value?.Dispose();
				}
        
        /// <summary>
				/// Commits a transaction.
				/// </summary>
				///
				///	<remarks>
				///	
				/// <RevisionHistory>
				/// Author				Date			                    Description
				/// DLGenerator			8/15/2025 2:57:18 AM				Created function
				/// 
				/// </RevisionHistory>
				/// 
				/// </remarks>
				//public static async Task CommitTransactionAsync()
				//{
				//    await DatabaseHelper.CommitTransactionAsync();

				//    DatabaseHelper.Dispose();
				//    DatabaseHelper = new();
				//}
    
        /// <summary>
        /// Rolls back and clears a transaction.
        /// </summary>
        ///
        ///	<remarks>
        ///	
        /// <RevisionHistory>
        /// Author				Date			                    Description
        /// DLGenerator			6/4/2026 10:07:11 PM				Created function
        /// 
        /// </RevisionHistory>
        /// 
        /// </remarks>
        public static void RollbackTransaction()
				{
				    _databaseHelper.Value?.RollbackTransaction();

				    _databaseHelper.Value?.Dispose();
				}
        
        /// <summary>
				/// Rolls back and clears a transaction.
				/// </summary>
				///
				///	<remarks>
				///	
				/// <RevisionHistory>
				/// Author				Date			                    Description
				/// DLGenerator			8/15/2025 2:57:18 AM				Created function
				/// 
				/// </RevisionHistory>
				/// 
				/// </remarks>
				//public static async Task RollbackTransactionAsync()
				//{
				//    await DatabaseHelper.RollbackTransactionAsync();

				//    DatabaseHelper.Dispose();
				//    DatabaseHelper = new();
				//}
    
        /// <summary>
        /// Copies the command from source database helper to internal transaction helper database helper.
        /// </summary>
        /// <param name="dbh"></param>
        /// <exception cref="NullReferenceException"></exception>
        private static void PrepareCommand(DatabaseHelper dbh)
        {
    		if (dbh.CurrentCommand is null)
    			throw new NullReferenceException(nameof(dbh.CurrentCommand));
    
            //Primary Server
    		DatabaseHelper!.CurrentCommand = dbh.CurrentCommand;
    		DatabaseHelper.CurrentCommand.Connection = DatabaseHelper.CurrentConnection;
            DatabaseHelper.CurrentCommand.Transaction = DatabaseHelper.CurrentTransaction;
    
            //Backup Server
            if (DatabaseHelper.ShouldUseBackupServer && dbh.CurrentBackupCommand is not null)
            {
    			DatabaseHelper.CurrentBackupCommand = dbh.CurrentBackupCommand;
    			DatabaseHelper.CurrentBackupCommand.Connection = DatabaseHelper.CurrentBackupConnection;
                DatabaseHelper.CurrentBackupCommand.Transaction = DatabaseHelper.CurrentBackupTransaction;
    		}
        }
    
    	/// <summary>
    	/// Relay ExecuteScalar call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal static object? ExecuteScalar(DatabaseHelper dbh)
        {
            PrepareCommand(dbh);
            return DatabaseHelper!.CurrentCommand!.ExecuteScalar(); //TODO: Figure out if we need to include backup server implementation here or if that will be done in the database helper methods
        }
    
    	/// <summary>
    	/// Relay ExecuteScalarAsync call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal static async Task<object?> ExecuteScalarAsync(DatabaseHelper dbh)
        {
            PrepareCommand(dbh);
            return await DatabaseHelper!.CurrentCommand!.ExecuteScalarAsync();
        }
    
    	/// <summary>
    	/// Relay ExecuteScalar call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal static object? ExecuteScalarOnBackupServer(DatabaseHelper dbh)
    	{
    		PrepareCommand(dbh);
    		return DatabaseHelper!.CurrentBackupCommand!.ExecuteScalar(); //TODO: Figure out if we need to include backup server implementation here or if that will be done in the database helper methods
    	}
    
    	/// <summary>
    	/// Relay ExecuteScalarAsync call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal static async Task<object?> ExecuteScalarOnBackupServerAsync(DatabaseHelper dbh)
    	{
    		PrepareCommand(dbh);
    		return await DatabaseHelper!.CurrentBackupCommand!.ExecuteScalarAsync();
    	}
    
    	/// <summary>
    	/// Relay ExecuteReader call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal static DbDataReader ExecuteReader(DatabaseHelper dbh)
        {
            PrepareCommand(dbh);
            return DatabaseHelper!.CurrentCommand!.ExecuteReader();
        }
    
    	/// <summary>
    	/// Relay ExecuteReaderAsync call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal async static Task<DbDataReader> ExecuteReaderAsync(DatabaseHelper dbh)
        {
            PrepareCommand(dbh);
            return await DatabaseHelper!.CurrentCommand!.ExecuteReaderAsync();
        }
    
    	/// <summary>
    	/// Relay ExecuteReader call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal static DbDataReader ExecuteReaderOnBackupServer(DatabaseHelper dbh)
    	{
    		PrepareCommand(dbh);
    		return DatabaseHelper!.CurrentBackupCommand!.ExecuteReader();
    	}
    
    	/// <summary>
    	/// Relay ExecuteReaderAsync call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal async static Task<DbDataReader> ExecuteReaderOnBackupServerAsync(DatabaseHelper dbh)
    	{
    		PrepareCommand(dbh);
    		return await DatabaseHelper!.CurrentBackupCommand!.ExecuteReaderAsync();
    	}
    
    	/// <summary>
    	/// Relay ExecuteNonQuery call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal static int ExecuteNonQuery(DatabaseHelper dbh)
        {
            PrepareCommand(dbh);
            return DatabaseHelper!.CurrentCommand!.ExecuteNonQuery();
        }
    
    	/// <summary>
    	/// Relay ExecuteNonQueryAsync call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal static async Task<int> ExecuteNonQueryAsync(DatabaseHelper dbh)
        {
            PrepareCommand(dbh);
            return await DatabaseHelper!.CurrentCommand!.ExecuteNonQueryAsync();
        }
    
    	/// <summary>
    	/// Relay ExecuteNonQuery call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal static int ExecuteNonQueryOnBackupServer(DatabaseHelper dbh)
    	{
    		PrepareCommand(dbh);
    		return DatabaseHelper!.CurrentBackupCommand!.ExecuteNonQuery();
    	}
    
    	/// <summary>
    	/// Relay ExecuteNonQueryAsync call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <returns></returns>
    	internal static async Task<int> ExecuteNonQueryOnBackupServerAsync(DatabaseHelper dbh)
    	{
    		PrepareCommand(dbh);
    		return await DatabaseHelper!.CurrentBackupCommand!.ExecuteNonQueryAsync();
    	}
    
    	/// <summary>
    	/// Relay ExecuteDataSet call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <param name="adapter"></param>
    	/// <returns></returns>
    	internal static DataSet ExecuteDataSet(DatabaseHelper dbh, DbDataAdapter adapter)
        {        
            var newDataSet = new DataSet();
    
            if (dbh.CurrentCommand is null)
                throw new NullReferenceException(nameof(dbh.CurrentCommand));
    
            adapter.SelectCommand = dbh.CurrentCommand;
            adapter.SelectCommand.Connection = DatabaseHelper!.CurrentConnection;
            adapter.SelectCommand.Transaction = DatabaseHelper.CurrentTransaction;
    
            adapter.Fill(newDataSet);
    
            return newDataSet;
        }
    
    	/// <summary>
    	/// Relay ExecuteDataSet call through TransactionHelper DatabaseHelper instance.
    	/// </summary>
    	/// <param name="dbh"></param>
    	/// <param name="adapter"></param>
    	/// <returns></returns>
    	internal static DataSet ExecuteDataSetOnBackupServer(DatabaseHelper dbh, DbDataAdapter adapter)
    	{
    		var newDataSet = new DataSet();
    
    		if (dbh.CurrentBackupCommand is null)
    			throw new NullReferenceException(nameof(dbh.CurrentBackupCommand));
    
    		adapter.SelectCommand = dbh.CurrentBackupCommand;
    		adapter.SelectCommand.Connection = DatabaseHelper!.CurrentBackupConnection;
    		adapter.SelectCommand.Transaction = DatabaseHelper.CurrentBackupTransaction;
    
    		adapter.Fill(newDataSet);
    
    		return newDataSet;
    	}
    }
    
    #endregion
    #region ExecutionResult
    
    public class ExecutionResult<T>
    {
        public ExecutionResult(T? result = default)
        {
            Result = result;
        }
    
        public bool WasSuccessful { get; set; } = false;
        public T? Result { get; set; } = default;
    }
    
    #endregion
    
	#region Metadata Classes
	public class TableColumnMetadata
	{
	    public string? TableName { get; set; }
	    public string? ColumnName { get; set; }
	    public string? DataType { get; set; }
	    public int? CharacterMaximumLength { get; set; }
	    public string? ColumnDefault { get; set; }
	}
	public class ViewMetadata
	{
	    public string? ViewName { get; set; }
	}
  
	public class TriggerMetadata
	{
	    public string? TriggerName { get; set; }
	    public string? TriggerType { get; set; }
	 }
	
  public class StoredProcedureMetadata
	{
	    public string? ProcedureName { get; set; }
	}
	#endregion
	#region SQLTimeRecorder
	internal static class SQLTimeRecorder
	{
		private const int MAX_BUFFER_SIZE = 7; // if the cache hits this number, we write to the database
        private static System.Threading.Timer? _timerCheckBuffer = default;
        private static int _checkIntervalInSeconds = 5;  // every 5 seconds we will write any contents to the database
        private static readonly object _lockObject = new();
        private static ConcurrentDictionary<string, ConcurrentBag<CachedSQLStatement>> _databases = new(); // we are storing the connection string if response table has been created)
	    
		internal static readonly string CREATE_DLGSQLRESPONSETIME_TABLE =
	        @"IF object_id('__dlgSQLResponseTime', 'U') is null
	            BEGIN
	                CREATE TABLE __dlgSQLResponseTime 
	                (
	                    [Key] UNIQUEIDENTIFIER DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
		                [DateTimeSent] [datetime2](7) NOT NULL,
		                [SQL] [varchar](MAX) NOT NULL,
		                [ElapsedResponseTimeInMilliseconds] [decimal](8, 3) NOT NULL
	                );
	            END 
	        ";
	
	    internal static readonly string CREATE_AVERAGERESPONSETIMEPERDAY_VIEW =
	            @"
	                CREATE VIEW __VdlgAverageResponseTimePerDay
	                AS
	                SELECT        
		                SQL, 
		                CAST(DateTimeSent AS DATE) AS Date, 
		                COUNT(*) AS NumberOfTimesExecuted, 
		                AVG(ElapsedResponseTimeInMilliseconds) AS AverageResponseTimeInMilliseconds
	                FROM 
		                __dlgSQLResponseTime
	                GROUP BY 
		                SQL, 
		                CAST(DateTimeSent AS DATE)
	        ";
	
	    internal static readonly string CREATE_AVERAGERESPONSETIMEPERHOUR_VIEW =
	        @"
	                CREATE VIEW __VdlgAverageResponseTimePerHour
	                AS
	                SELECT 
		                SQL, 
		                COUNT(*) AS NumberOfTimesExecuted, 
		                DATEPART(YEAR, DateTimeSent) AS Year, 
		                DATEPART(MONTH, DateTimeSent) AS Month, 
		                DATEPART(DAY, DateTimeSent) AS Day, 
		                DATEPART(HOUR, DateTimeSent) AS Hour, 
		                AVG(ElapsedResponseTimeInMilliseconds) AS AverageResponseTimeInMilliseconds
	                FROM            
		                [__dlgSQLResponseTime]
	                GROUP BY 
		                SQL, 
		                DATEPART(YEAR, DateTimeSent),
		                DATEPART(MONTH, DateTimeSent), 
		                DATEPART(DAY, DateTimeSent), 
		                DATEPART(HOUR, DateTimeSent)
	        ";
			
		internal static readonly string CREATE_AVERAGERESPONSETIMEPERMINUTE_VIEW =
            @"
	            CREATE VIEW __VdlgAverageResponseTimePerMinute
	            AS
	            SELECT 
		            SQL, 
		            COUNT(*) AS NumberOfTimesExecuted, 
		            DATEPART(YEAR, DateTimeSent) AS Year, 
		            DATEPART(MONTH, DateTimeSent) AS Month, 
		            DATEPART(DAY, DateTimeSent) AS Day, 
		            DATEPART(HOUR, DateTimeSent) AS Hour, 
		            DATEPART(MINUTE, DateTimeSent) AS Minute, 
		            AVG(ElapsedResponseTimeInMilliseconds) AS AverageResponseTimeInMilliseconds
	            FROM            
		            [__dlgSQLResponseTime]
	            GROUP BY 
		            SQL, 
		            DATEPART(YEAR, DateTimeSent),
		            DATEPART(MONTH, DateTimeSent), 
		            DATEPART(DAY, DateTimeSent), 
		            DATEPART(HOUR, DateTimeSent),
		            DATEPART(MINUTE, DateTimeSent)
	        ";
	
	    internal static readonly string CREATE_AVERAGERESPONSETIMEPERSQL_VIEW =
	            @"
	                CREATE VIEW __VdlgAverageResponseTimePerSQL AS
	                SELECT TOP 100 PERCENT
	                    SQL,
	                    COUNT(*) AS NumberOfTimesExecuted, 
	                    AVG(ElapsedResponseTimeInMilliseconds) AS AverageResponseTimeInMilliseconds
	                FROM 
	                    __dlgSQLResponseTime
	                GROUP BY 
	                    SQL
	        ";
	
	    // WILL NOT WORK FOR VIEWS FOR SOME REASON
	    //internal static string CREATE_AVERAGERESPONSETIMEPERSQL_VIEW =
	    //        @"IF object_id('VdlgAverageResponseTimePerSQL', 'V') is null
	    //        BEGIN
	    //            CREATE VIEW VdlgAverageResponseTimePerSQL AS
	    //            SELECT TOP 100 PERCENT
	    //                SQL,
	    //             COUNT(*) AS NumberOfTimesExecuted, 
	    //                AVG(ElapsedResponseTimeInMilliseconds) AS AverageResponseTimeInMilliseconds
	    //            FROM 
	    //                __dlgSQLResponseTime
	    //            GROUP BY 
	    //                SQL
	    //        END 
	    //    ";
		
		static SQLTimeRecorder()
        {
            // Create a timer that invokes CheckBuffer after the first x seconds and every y seconds thereafter.
            _timerCheckBuffer = new System.Threading.Timer(CheckBufferAsync!, null, _checkIntervalInSeconds * 1000, _checkIntervalInSeconds * 1000);
        }
	
	/// <summary>
	/// This method will only execute its code one time in order to create the necessary table and views.  Calling
	/// it subsequent times will do nothing.
	/// </summary>
	private static bool BuildResponseTableAndViews(SupportedDatabases? database = null, string? connectionString = null)
        {
            DatabaseHelper? _dh = null;
            bool wasSuccessful = false;  // assume worst

            try
            {
                if (database is null || connectionString is null)
                {
                    _dh = new DatabaseHelper()
                    {
                        ShouldSaveSQLResponseTimes = false, // if true, this would cause a never ending loop and crash the app
												ShouldUseBackupServer = false
                    };
                }
                else
                {
                    _dh = new DatabaseHelper(connectionString, databaseToUse: database!.Value)
                    {
                        ShouldSaveSQLResponseTimes = false,
												ShouldUseBackupServer = false
                    };
                }

                var result = _dh.ExecuteNonQuery(SQLTimeRecorder.CREATE_DLGSQLRESPONSETIME_TABLE, CommandType.Text);
                if (result.WasSuccessful)
                {
                    wasSuccessful = true;  // as long as the table was created, we can record our times.
                    // create views
                    try
                    {
                        var getViewsResult = _dh.GetViews();
                        if (getViewsResult.WasSuccessful)
                        {
                            if (getViewsResult.Result is not null)
                            {
                                List<ViewMetadata> views = getViewsResult.Result;
                                string viewName = "__VdlgAverageResponseTimePerSQL";

                                if (views.Any(x => x.ViewName == viewName))
                                {
                                    // drop the view before adding it
                                    _dh.ExecuteNonQuery($"DROP VIEW {viewName};", CommandType.Text);
                                }
                                _dh.ExecuteNonQuery(CREATE_AVERAGERESPONSETIMEPERSQL_VIEW, CommandType.Text);

                                viewName = "__VdlgAverageResponseTimePerDay";
                                if (views.Any(x => x.ViewName == viewName))
                                {
                                    // drop the view before adding it
                                    _dh.ExecuteNonQuery($"DROP VIEW {viewName};", CommandType.Text);
                                }
                                _dh.ExecuteNonQuery(CREATE_AVERAGERESPONSETIMEPERDAY_VIEW, CommandType.Text);

                                viewName = "__VdlgAverageResponseTimePerHour";
                                if (views.Any(x => x.ViewName == viewName))
                                {
                                    // drop the view before adding it
                                    _dh.ExecuteNonQuery($"DROP VIEW {viewName};", CommandType.Text);
                                }
                                _dh.ExecuteNonQuery(CREATE_AVERAGERESPONSETIMEPERHOUR_VIEW, CommandType.Text);

                                viewName = "__VdlgAverageResponseTimePerMinute";
                                if (views.Any(x => x.ViewName == viewName))
                                {
                                    // drop the view before adding it
                                    _dh.ExecuteNonQuery($"DROP VIEW {viewName};", CommandType.Text);
                                }
                                _dh.ExecuteNonQuery(CREATE_AVERAGERESPONSETIMEPERMINUTE_VIEW, CommandType.Text);

                            }
                        }
                        else
                        {
                            // Could not create the views, no big deal
                        }
                    }
                    catch
                    {
                        // eat the exception, don't have to have views
                        //
                    }
                }

            }
            catch (Exception)
            {
                // TODO: Log this ?        
            }

            return wasSuccessful;
        }
		
		internal static void RecordSQLResponseTime(SupportedDatabases database, string connectionString, string sqlStatement, double elapsedResponseTimeInMilliseconds)
        {
            CachedSQLStatement cachedSQL;
            ConcurrentBag<CachedSQLStatement> cachedSQLStatements = [];

            StopTimer();  // we do not want the timer to fire and start saving to the database while we are evaluating the buffer 

            // First check our dictionary to make sure this connection string as been initialized (i.e. response table created)

            if (_databases.ContainsKey(connectionString))
            {
                cachedSQLStatements = _databases[connectionString];
            }
            else
            {
                // this is the first time seeing this connection string so create the tables and add an entry
                BuildResponseTableAndViews(database, connectionString);
                _databases.TryAdd(connectionString, cachedSQLStatements);
            }

            cachedSQL = new CachedSQLStatement(database, connectionString, sqlStatement, elapsedResponseTimeInMilliseconds);
            cachedSQLStatements.Add(cachedSQL);

            SendSQLStatementsToDatabase();

            StartTimer();
        }

        internal record CachedSQLStatement(SupportedDatabases Database, string ConnectionString, string SqlStatement, double ElapsedTimeInMilliseconds)
        {
            public DateTime DateTimeAdded = DateTime.Now;
        }

        private static void SendSQLStatementsToDatabase()
        {
            // we don't know how long this will take so disable our timer while processing so that we don't step on ourselves while copying files.
            StopTimer();

            lock (_lockObject)
            {
                // only one thread at a time can write the buffer to the database
                if (!TransactionHelper.IsInTransactionMode && _databases.Count > 0)
                {
                    // cycle through all the various databases and process any cached sql statements
                    foreach (var database in _databases)
                    {
                        var cachedSQLStatements = database.Value;

                        // build one large sql string of multiple inserts statements from all the entries in the 
                        // Sorting the entries
                        var sortedEntries = cachedSQLStatements
                            .OrderBy(entry => entry.DateTimeAdded)
                            .Take(MAX_BUFFER_SIZE)
                            .ToList();

                        StringBuilder sb = new();

                        foreach (var entry in sortedEntries)
                        {

                            string sqlInsertStatement =
                                $"INSERT INTO __dlgSQLResponseTime (DateTimeSent,SQL,ElapsedResponseTimeInMilliseconds) VALUES ('{entry.DateTimeAdded}', '{entry.SqlStatement}', {entry.ElapsedTimeInMilliseconds})";

                            // special case. batching will not work if the database name is Default.  Single quotes fail:
                            sqlInsertStatement = sqlInsertStatement.Replace("'Default'", "''Default''");

                            sb.Append(sqlInsertStatement);
                            sb.Append(';');
                        }

                        if (sb.Length > 0)
                        {

                            // we have built a string of all sql statements so send batched sql statements to the database
                            try
                            {
                                // all entries for this database will have the same connection string and database on each
                                // cached statement.  We only need to use the first one.

                                SupportedDatabases databaseToUse = database.Value.First().Database;
                                string connectString = database.Value.First().ConnectionString;

                                var dh = new DatabaseHelper(connectString, databaseToUse: databaseToUse)
                                {
                                    ShouldSaveSQLResponseTimes = false,
																		ShouldUseBackupServer = false
                                };

                                //string sqlInsertStatement =
                                //    $"INSERT INTO __dlgSQLResponseTime (DateTimeSent,SQL,ElapsedResponseTimeInMilliseconds) VALUES ({entry.DateTimeAdded}, {entry.SqlStatement}, {entry.ElapsedTimeInMilliseconds})";

                                var sql = sb.ToString();
                                var executionResult = dh.ExecuteNonQuery(sql, CommandType.Text);

                            }
                            catch
                            {
                                // eat any error and don't save sql
                            }
                        }
                    }

                    // now that we have processed all the cached values and we do not know if we will
                    // receive any further statements for those databases, so clear them from memory.
                    _databases.Clear();

                }
            }
            // activate our timer again
            StartTimer();
        }


        private static void StopTimer()
        {
            _timerCheckBuffer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private static void StartTimer()
        {
            _timerCheckBuffer?.Change(_checkIntervalInSeconds * 1000, _checkIntervalInSeconds * 1000);
        }

        /// <summary>
        /// Checks for any updates at the location provided.
        /// </summary>
        /// <param name="state">required by the timer delegate</param>
        private static async void CheckBufferAsync(object state)
        {
            // we don't know how long this will take so disable our timer while processing so that we don't step on ourselves while copying files.
            _timerCheckBuffer!.Change(Timeout.Infinite, Timeout.Infinite);

            if (_databases is not null && _databases.Count > 0)
            {
                SendSQLStatementsToDatabase();
            }
            await Task.Delay(500);

            // activate our timer again
            _timerCheckBuffer.Change(_checkIntervalInSeconds * 1000, _checkIntervalInSeconds * 1000);
        }
	
	}
	#endregion
	
	#region Query Enhancements
	
	/// <summary>
    /// Sql comparison operators
    /// </summary>
    public enum Operator
    {
        GreaterThan,
        GreaterThanEqual,
        LessThan,
        LessThanEqual,
        NotEquals,
        Like,
        Equal,
        In
    }
    /// <summary>
    /// Sql direction operators
    /// </summary>
    public enum Direction
    {
        Asc,
        Desc
    }
    /// <summary>
    /// Ends the query builing process
    /// </summary>
    public interface IQuery
    {
        Query Build();
    }
    public interface IBaseQueryBuilder : IOrderStage, IWhereStage<ILogicalOperatorStage>
    {
    }
    public interface IConditionalQueryBuilder : IWhereStage<ILogicalOperatorStageDelete>
    {
    }
    /// <summary>
    /// Interface for order by statement
    /// </summary>
    public interface IOrderStage : IQuery
    {
        IOrderStage OrderBy(string column, Direction direction = Direction.Asc);
    }
    /// <summary>
    /// Interface for where statement
    /// </summary>
    public interface IWhereStage<T>
    {
        T Where(string column, Operator condition, object value);
		T Where(string column, Operator condition, params object[] values);
    }
    public interface IBaseLogicalOperatorStage<T>
    {
        T And(string column, Operator condition, object value);
        T Or(string column, Operator condition, object value);
        T Not(string column, Operator condition, object value);
    }
    /// <summary>
    /// Interface for logical operation's statements with order by
    /// </summary>
    public interface ILogicalOperatorStage : IOrderStage, IBaseLogicalOperatorStage<ILogicalOperatorStage>
    {

    }
    /// <summary>
    /// Interface for logical operation's statements without order by
    /// </summary>
    public interface ILogicalOperatorStageDelete : IBaseLogicalOperatorStage<ILogicalOperatorStageDelete>, IQuery
    {

    }
    /// <summary>
    /// Helper class for query builder
    /// </summary>
    public abstract class QueryBuilderHelper
    {
        // Helper method to format the value correctly for SQL
        public virtual string FormatValue(object value)
        {
            return value switch
            {
                null or DBNull => "NULL",
                string s => $"'{EscapeSqlString(s)}'",
                DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
                bool b => b ? "1" : "0",
                Guid guid => $"'{guid}'",
                _ => value?.ToString() ?? "NULL"  // Numeric values remain as is
            };
        }

        // Helper method to escape single quotes in strings for SQL
        public virtual string EscapeSqlString(string value)
        {
            return value.Replace("'", "''");
        }

        public virtual string GetOperatorString(Operator condition)
        {
            return condition switch
            {
                Operator.GreaterThan => ">",
                Operator.LessThan => "<",
                Operator.Like => "LIKE",
                Operator.Equal => "=",
                Operator.LessThanEqual => "<=",
                Operator.GreaterThanEqual => ">=",
                Operator.NotEquals => "<>",
				Operator.In => "IN",
                _ => throw new ArgumentException("Invalid operator")
            };
        }
        public virtual string GetDirectionString(Direction direction)
        {
            return direction switch
            {
                Direction.Asc => "ASC",
                Direction.Desc => "DESC",
                _ => throw new ArgumentException("Invalid operator")
            };
        }
        public virtual string FormatNullCondition(string columnName, string operatorString, object value)
        {
            string formatedCondition = string.Empty;
            if (value is null || value == DBNull.Value)
            {
                if (operatorString.Equals("=", StringComparison.OrdinalIgnoreCase))
                {
                    formatedCondition = $"{columnName} IS NULL";//converting = to IS
                }
                else if (operatorString.Equals("<>", StringComparison.OrdinalIgnoreCase))
                {
                    formatedCondition = $"{columnName} IS NOT NULL";//converting <> to IS NOT
                }
                else
                {
                    throw new ArgumentException("Invalid condition for null value comparison");
                }
            }
            return formatedCondition;
        }
    }
    /// <summary>
    /// Class that implements IBaseQueryBuilder interfaces
    /// </summary>
    public class QueryBuilder : QueryBuilderHelper, IBaseQueryBuilder
    {
        private readonly Query _query = new Query();

        // Allows OrderBy to be called independently, returns self (fluent chaining)
        public IOrderStage OrderBy(string column, Direction direction = Direction.Asc)
        {
            var directionString = GetDirectionString(direction);
            _query.SetOrderBy(column, directionString);
            return this;
        }

        /// <summary>
        /// Create a WHERE clause with a single value
        /// </summary>
        /// <param name="column">column name</param>
        /// <param name="condition">condition</param>
        /// <param name="value"></param>
        /// <returns></returns>
        public ILogicalOperatorStage Where(string column, Operator condition, object value)
        {
            var operatorString = GetOperatorString(condition);
            string formatedConditionForNull = FormatNullCondition(column, operatorString, value);
            if (!string.IsNullOrEmpty(formatedConditionForNull))
            {
                _query.AddCondition(formatedConditionForNull);
            }
            else
            {
                _query.AddCondition($"[{column}] {operatorString} {FormatValue(value)}");
            }
            return new LogicalOperatorStage(_query);
        }
		
		/// <summary>
        /// Create a WHERE clause with multiple values for the same column
        /// </summary>
        /// <param name="column">column name</param>
        /// <param name="condition">condition</param>
        /// <param name="values">array of values, only applicable for condition having IN</param>
        /// <returns></returns>
        public ILogicalOperatorStage Where(string column, Operator condition, params object[] values)
        {
            if (condition is not Operator.In)
            {
                return Where(column, condition, values[0]);
            }

            var operatorString = GetOperatorString(condition);
            var formattedValues = values
            .Where(v => v is not null && v != DBNull.Value) // Exclude NULLs initially
            .Select(FormatValue)
            .ToList();

            string conditionString = $"[{column}] {operatorString} ({string.Join(", ", formattedValues)})";

            // Handle NULL values in `IN` clause separately
            if (values.Any(v => v is null || v == DBNull.Value))
            {
                conditionString += $" OR [{column}] IS NULL";
            }

            _query.AddCondition(conditionString);
            return new LogicalOperatorStage(_query);
        }

        // Finalize the query building process
        public Query Build()
        {
            return _query;
        }
    }
    public class ConditionalQueryBuilder : QueryBuilderHelper, IConditionalQueryBuilder, IQuery
    {
        private readonly Query _query = new Query();

        /// <summary>
        /// Create a WHERE clause with a single value
        /// </summary>
        /// <param name="column">column name</param>
        /// <param name="condition">condition</param>
        /// <param name="value"></param>
        /// <returns></returns>
        public ILogicalOperatorStageDelete Where(string column, Operator condition, object value)
        {
            var operatorString = GetOperatorString(condition);
            string formatedConditionForNull = FormatNullCondition(column, operatorString, value);
            if (!string.IsNullOrEmpty(formatedConditionForNull))
            {
                _query.AddCondition(formatedConditionForNull);
            }
            else
            {
                _query.AddCondition($"[{column}] {operatorString} {FormatValue(value)}");
            }
            return new LogicalOperatorStageDelete(_query);
        }
		
		/// <summary>
        /// Create a WHERE clause with multiple values for the same column
        /// </summary>
        /// <param name="column">column name</param>
        /// <param name="condition">condition</param>
        /// <param name="values">array of values, only applicable for condition having IN</param>
        /// <returns></returns>
        public ILogicalOperatorStageDelete Where(string column, Operator condition, params object[] values)
        {
            if (condition is not Operator.In)
            {
                return Where(column, condition, values[0]);
            }

            var operatorString = GetOperatorString(condition);
            var formattedValues = values
            .Where(v => v is not null && v != DBNull.Value) // Exclude NULLs initially
            .Select(FormatValue)
            .ToList();

            string conditionString = $"[{column}] {operatorString} ({string.Join(", ", formattedValues)})";

            // Handle NULL values in `IN` clause separately
            if (values.Any(v => v is null || v == DBNull.Value))
            {
                conditionString += $" OR [{column}] IS NULL";
            }

            _query.AddCondition(conditionString);
            return new LogicalOperatorStageDelete(_query);
        }

        // Finalize the query building process
        public Query Build()
        {
            return _query;
        }
    }
    /// <summary>
    /// Class that implements ILogicalOperatorStage interface
    /// </summary>
    public class LogicalOperatorStage : QueryBuilderHelper, ILogicalOperatorStage
    {
        private readonly Query _query;

        public LogicalOperatorStage(Query query)
        {
            _query = query;
        }

        public ILogicalOperatorStage And(string column, Operator condition, object value)
        {
            var operatorString = GetOperatorString(condition);
            string formatedConditionForNull = FormatNullCondition(column, operatorString, value);
            if (!string.IsNullOrEmpty(formatedConditionForNull))
            {
                _query.AddCondition(formatedConditionForNull, "AND");
            }
            else
            {
                _query.AddCondition($"[{column}] {operatorString} {FormatValue(value)}", "AND");
            }
            return this;
        }

        public ILogicalOperatorStage Or(string column, Operator condition, object value)
        {
            var operatorString = GetOperatorString(condition);
            string formatedConditionForNull = FormatNullCondition(column, operatorString, value);
            if (!string.IsNullOrEmpty(formatedConditionForNull))
            {
                _query.AddCondition(formatedConditionForNull, "OR");
            }
            else
            {
                _query.AddCondition($"[{column}] {operatorString} {FormatValue(value)}", "OR");
            }
            return this;
        }
        public ILogicalOperatorStage Not(string column, Operator condition, object value)
        {
            var operatorString = GetOperatorString(condition);
            string formatedConditionForNull = FormatNullCondition(column, operatorString, value);
            if (!string.IsNullOrEmpty(formatedConditionForNull))
            {
                _query.AddCondition(formatedConditionForNull, "NOT");
            }
            else
            {
                _query.AddCondition($"[{column}] {operatorString} {FormatValue(value)}", "NOT");
            }
            return this;
        }

        public IOrderStage OrderBy(string column, Direction direction = Direction.Asc)
        {
            var directionString = GetDirectionString(direction);
            _query.SetOrderBy(column, directionString);
            return this;
        }

        //Finalize the query building process
        public Query Build()
        {
            return _query;
        }
    }

    /// <summary>
    /// Class that implements ILogicalOperatorStageDelete interface
    /// </summary>
    public class LogicalOperatorStageDelete : QueryBuilderHelper, ILogicalOperatorStageDelete
    {
        private readonly Query _query;

        public LogicalOperatorStageDelete(Query query)
        {
            _query = query;
        }

        public ILogicalOperatorStageDelete And(string column, Operator condition, object value)
        {
            var operatorString = GetOperatorString(condition);
            string formatedConditionForNull = FormatNullCondition(column, operatorString, value);
            if (!string.IsNullOrEmpty(formatedConditionForNull))
            {
                _query.AddCondition(formatedConditionForNull, "AND");
            }
            else
            {
                _query.AddCondition($"[{column}] {operatorString} {FormatValue(value)}", "AND");
            }
            return this;
        }

        public ILogicalOperatorStageDelete Or(string column, Operator condition, object value)
        {
            var operatorString = GetOperatorString(condition);
            string formatedConditionForNull = FormatNullCondition(column, operatorString, value);
            if (!string.IsNullOrEmpty(formatedConditionForNull))
            {
                _query.AddCondition(formatedConditionForNull, "OR");
            }
            else
            {
                _query.AddCondition($"[{column}] {operatorString} {FormatValue(value)}", "OR");
            }
            return this;
        }
        public ILogicalOperatorStageDelete Not(string column, Operator condition, object value)
        {
            var operatorString = GetOperatorString(condition);
            string formatedConditionForNull = FormatNullCondition(column, operatorString, value);
            if (!string.IsNullOrEmpty(formatedConditionForNull))
            {
                _query.AddCondition(formatedConditionForNull, "NOT");
            }
            else
            {
                _query.AddCondition($"[{column}] {operatorString} {FormatValue(value)}", "NOT");
            }
            return this;
        }

        //Finalize the query building process
        public Query Build()
        {
            return _query;
        }
    }

    /// <summary>
    /// Class that build the final sql query
    /// </summary>
    public class Query
    {
        private List<(string Condition, string Operator)> _conditions = new List<(string, string)>();
        private string? _orderBy;

        public void AddCondition(string condition, string logicalOperator = "AND")
        {
            if (_conditions.Count is 0)
            {
                // The first condition should not have an operator before it
                logicalOperator = string.Empty;
            }
            _conditions.Add((condition, logicalOperator));
        }

        public void SetOrderBy(string column, string direction)
        {
            _orderBy = $"[{column}] {direction}";
        }

        public override string ToString()
        {
            // Build the WHERE clause by concatenating conditions with their logical operators
            var whereClause = string.Join(" ", _conditions.Select(c => $"{c.Operator} {c.Condition}".Trim()));

            // Start building the query string
            string query = string.Empty;

            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                query += $"WHERE {whereClause}";
            }

            // Add ORDER BY clause if it exists
            if (!string.IsNullOrEmpty(_orderBy))
            {
                query += $" ORDER BY {_orderBy}";
            }

            return query;
        }
    }
	
#endregion
	
	#region TrackableEntity
		[AttributeUsage(AttributeTargets.Property)]
		public class TrackableAttribute : Attribute {}

		/// <summary>
    /// Base class for entities that support tracking of property values.
    /// </summary>
    public abstract class TrackableEntity<T> where T : TrackableEntity<T>
    {
        private static readonly (string Name, Func<T, object?> Getter)[] _trackedProps;

        static TrackableEntity()
        {
            _trackedProps = typeof(T)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.IsDefined(typeof(TrackableAttribute), true))
                .Select(p => (p.Name, BuildGetter(p)))
                .ToArray();
        }

        /// <summary>
        /// Builds and compiles a delegate that retrieves the value of the specified property from an instance of <typeparamref name="T"/>.
        /// The generated delegate reads the property, boxes value types to <see cref="object"/>, and returns the value as <c>object?</c>.
        /// </summary>
        /// <param name="prop">The <see cref="PropertyInfo"/> representing a readable instance property on <typeparamref name="T"/>.</param>
        /// <returns>A compiled <see cref="Func{T, TResult}"/> delegate that returns the property value (boxed to <see cref="object"/> when necessary).</returns>
        private static Func<T, object?> BuildGetter(PropertyInfo prop)
        {
            var param = System.Linq.Expressions.Expression.Parameter(typeof(T), "x");
            var body = System.Linq.Expressions.Expression.Convert(
                System.Linq.Expressions.Expression.Property(param, prop),
                typeof(object)
            );
            return System.Linq.Expressions.Expression.Lambda<Func<T, object?>>(body, param).Compile();
        }

        private Dictionary<string, object?>? _originalValues;

        /// <summary>
        /// Takes a snapshot of the current property values.
        /// </summary>
        public void TakeSnapshot()
        {
            _originalValues = new Dictionary<string, object?>(_trackedProps.Length);
            foreach (var (name, getter) in _trackedProps)
                _originalValues[name] = getter((T)this);
        }
		
        /// <summary>
        /// Gets the & values of properties that have changed since the last snapshot.
        /// </summary>
        /// <returns>Return a list of properties that have been changed</returns>
        public IEnumerable<(string Name, object? Value)> GetChangedPropertiesWithValues()
        {
            if (_originalValues is null || _originalValues.Count == 0)
                yield break;

            foreach (var (name, getter) in _trackedProps)
            {
                var currentValue = getter((T)this);
                var originalValue = _originalValues.TryGetValue(name, out var v) ? v : null;
                if (!Equals(currentValue, originalValue))
                    yield return (name, currentValue);
            }
        }
		
        /// <summary>
        /// Clears the snapshot of original values.
        /// </summary>
        public void ClearSnapshot() => _originalValues?.Clear();
    }
#endregion
	
    #endregion
    
    public static class ObjectExtensions
    {
        public static T OrIfNullOrEmpty<T>(this T value, T fallback)
        {
            if (value == null)
                return fallback;

            if (value is string s)
                return string.IsNullOrWhiteSpace(s) ? fallback : value;

            if (value is Guid g)
                return g == Guid.Empty ? fallback : value;

            if (value is DateTime dt)
                return dt == DateTime.MinValue ? fallback : value;

            var type = typeof(T);
            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                // A Nullable<> with no value was handled by the null check above. Also treat one
                // holding the underlying default (e.g. an int? of 0 — an unassigned identity key) as
                // empty, so primary-key WHERE clauses fall back to the live key instead of matching 0.
                var underlyingDefault = underlyingType.IsValueType ? Activator.CreateInstance(underlyingType) : null;
                if (object.Equals(value, underlyingDefault))
                    return fallback;
            }

            if (type.IsValueType && value.Equals(default(T)))
                return fallback;

            // Otherwise — not null, not empty, return as is
            return value;
        }
    }
    
    }
