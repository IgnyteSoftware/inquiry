//
// Class	:	RegionBase.cs
// Author	:  	Inquiry © 2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:11 PM
//

using System;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Threading.Tasks;

namespace Inquiry.Benchmarks.DLG
{

	/// <summary>
	/// Class for the properties of the object
	/// </summary>
	public partial class RegionFields
	{
		public const string RegionID                  = "RegionID";
		public const string RegionDescription         = "RegionDescription";
	}
	
	/// <summary>
	/// Data access class for the "Region" table.
	/// </summary>
	[Serializable]
	public class RegionBase : TrackableEntity<RegionBase>
	{
		
		#region Class Level Variables
		
		private DatabaseHelper? _databaseHelper = null;
    
		private int            	_regionID                	= 0;
		private int            ?	_originalRegionID        	= 0;
		private string         	_regionDescription       	= string.Empty;

		private Territories? _territoriesRegionID = null;
		
		#endregion
		
        #region DatabaseHelper Properties
    
		public static int CommandTimeOut
		{
			get; set;
		}

        #endregion
    
        #region Constants
	  	
		#endregion
		
		#region Constructors / Destructors

		/// <summary>
		/// Class Constructor
		///</summary>
		public RegionBase(DatabaseHelper? databaseHelper = null) { 
                _databaseHelper = databaseHelper;
                TakeSnapshot();
          }
					
		#endregion
		
		#region Properties

		
		/// <summary>
		/// Returns the identifier of the persistent object. Mandatory.
		/// </summary>
		[Trackable]
		public int RegionID
		{
			get 
			{ 
				return _regionID;
			}
			set 
			{
			
				if (_originalRegionID is null || !_originalRegionID.HasValue)
						_originalRegionID = _regionID;
				_regionID = value; 
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "RegionDescription" field. Length must be between 0 and 60 characters. Mandatory.
		/// </summary>
		[Trackable]
		public string RegionDescription
		{
			get 
			{ 
				return _regionDescription.Trim();
			}
			set 
			{
				
				if (value is null)
					throw new ArgumentNullException("value", "Value is null.");
				
				if (value is not null && value.Length > 60)
					throw new ArgumentException("RegionDescription length must be between 0 and 60 characters.");
				
				if (value is not null)
				{		           
					_regionDescription = value.Trim(); 
				}
			}
		}
      

		/// <summary>
		/// Provides access to the related table 'Territories'
		/// </summary>
		public Territories? TerritoriesUsingRegionID
		{
			get 
			{
				if (_territoriesRegionID is null)
				{
					_territoriesRegionID = new Territories();
					_territoriesRegionID = Territory.SelectAllByForeignKeyRegionID(new RegionPrimaryKey(RegionID), _databaseHelper);
				}                
				return _territoriesRegionID; 
			}
			set 
			{
				  _territoriesRegionID = value;
			}
		}		
		//This property is related to the table name that exist in database
		
		public static string TableName
		{
			get 
			{ 
				  return "Region";
			}
		}
      

		#endregion
		
		#region Methods (Public)

		/// <summary>
		/// This method will insert one new row into the database using the property Information
		/// </summary>
		/// <param name="getBackValues" type="bool">Should re-populate values returned from database.</param>
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public virtual bool Insert(bool getBackValues = false)
		{
			
			bool wasExecutionSuccessful = false;      
			DbDataReader? dr = null;
			DatabaseHelper dh = new DatabaseHelper(_databaseHelper);
			dh.CommandTimeOut = CommandTimeOut;
			
			try
			{
			
			

				PopulateDatabaseHelperParameters(dh);
			
				if (!getBackValues && !dh.ShouldUseBackupServer)
				{
					var executionResult = dh.ExecuteScalar("gsp_Region_Insert");
					wasExecutionSuccessful = executionResult.WasSuccessful;
					if (wasExecutionSuccessful)
                    {
                        TakeSnapshot();
                    }
				}
				else //Try Primary Server
				{
					var executionResult = dh.ExecuteReader("gsp_Region_Insert");
					dr = executionResult.Result!;
					wasExecutionSuccessful = executionResult.WasSuccessful;  
					if (dr.Read())
					{
						PopulateObjectFromReader(this, dr);
					}
					else
					{
						TakeSnapshot();
					}
					dr.Close();
				}
			
			}
			catch (Exception ex)
			{
			     dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
			    dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will asynchronously insert one new row into the database using the property Information
		/// </summary>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="getBackValues" type="bool">Should re-populate values returned from database.</param>
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public virtual async Task<bool> InsertAsync(bool getBackValues = false, CancellationToken cancellationToken = default) 
		{
			
			bool wasExecutionSuccessful = false;      
			DbDataReader? dr = null;
			DatabaseHelper dh = new DatabaseHelper(_databaseHelper);
			dh.CommandTimeOut = CommandTimeOut;
			
			try
			{
			
			

				PopulateDatabaseHelperParameters(dh);
				
			    if (!getBackValues && !dh.ShouldUseBackupServer)
			    {
				     var executionResult = await dh.ExecuteScalarAsync("gsp_Region_Insert", cancellationToken);
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server 
			    {
				      var executionResult = await dh.ExecuteReaderAsync("gsp_Region_Insert", cancellationToken);
					  dr = executionResult.Result!;
					  wasExecutionSuccessful = executionResult.WasSuccessful;
				      if (dr.Read())
				      {
                            PopulateObjectFromReader(this, dr);
				      }
					  else
					  {
					  	TakeSnapshot();
					  }
					  dr.Close();
			    }
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
			    dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will Update one new row into the database using the property Information
		/// </summary>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public virtual bool Update() 
		{
			
            
            DatabaseHelper dh = new DatabaseHelper(_databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			var table = new DataTable("Table");
			table.Columns.Add("ColumnName", typeof(string));
            table.Columns.Add("NewValue", typeof(string));

			try
			{
			
			
				// Pass the value of '_regionID' as parameter 'RegionID' of the stored procedure.
				dh.AddParameter("@RegionID", _originalRegionID.OrIfNullOrEmpty(_regionID));
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

				foreach (var (propName, value) in GetChangedPropertiesWithValues())
				{
				    var row = table.NewRow();
				    row["ColumnName"] = propName;
				    row["NewValue"] = value ?? DBNull.Value;
				    table.Rows.Add(row);
				}
				
				string xml;
                using (var sw = new StringWriter())
                {
                    table.WriteXml(sw, XmlWriteMode.IgnoreSchema, false);
                    xml = sw.ToString();
                }

                dh.AddParameter("@GenericUpdateInstructionXml", xml, DbType.Xml);
				
                //Try Primary Server
                var executionResult = dh.ExecuteScalar("gsp_Region_Update");
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
			}
            catch
            {
                throw;
            }
            finally
            {
                dh.Dispose();
            }
			ClearSnapshot();
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will asynchronously Update one new row into the database using the property Information
		/// </summary>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public virtual async Task<bool> UpdateAsync() 
		{
			
            DatabaseHelper dh = new DatabaseHelper(_databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			var table = new DataTable("Table");
			table.Columns.Add("ColumnName", typeof(string));
			table.Columns.Add("NewValue", typeof(string));
			
			try
			{
			
			
				// Pass the value of '_regionID' as parameter 'RegionID' of the stored procedure.
				dh.AddParameter("@RegionID", _originalRegionID.OrIfNullOrEmpty(_regionID));
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
			
				foreach (var (propName, value) in GetChangedPropertiesWithValues())
				{
				    var row = table.NewRow();
				    row["ColumnName"] = propName;
				    row["NewValue"] = value ?? DBNull.Value;
				    table.Rows.Add(row);
				}
				
				string xml;
				using (var sw = new StringWriter())
				{
				    table.WriteXml(sw, XmlWriteMode.IgnoreSchema, false);
				    xml = sw.ToString();
				}
				
				dh.AddParameter("@GenericUpdateInstructionXml", xml, DbType.Xml);

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Region_Update");
                wasExecutionSuccessful = executionResult.WasSuccessful;
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			ClearSnapshot();
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will asynchronously Update one new row into the database using the property Information
		/// </summary>
		///
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public virtual async Task<bool> UpdateAsync(CancellationToken cancellationToken) 
		{
			
            DatabaseHelper dh = new DatabaseHelper(_databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			var table = new DataTable("Table");
			table.Columns.Add("ColumnName", typeof(string));
			table.Columns.Add("NewValue", typeof(string));

			try
			{
			
			
				// Pass the value of '_regionID' as parameter 'RegionID' of the stored procedure.
				dh.AddParameter("@RegionID", _originalRegionID.OrIfNullOrEmpty(_regionID));
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
			
				foreach (var (propName, value) in GetChangedPropertiesWithValues())
				{
				    var row = table.NewRow();
				    row["ColumnName"] = propName;
				    row["NewValue"] = value ?? DBNull.Value;
				    table.Rows.Add(row);
				}
				
				string xml;
				using (var sw = new StringWriter())
				{
				    table.WriteXml(sw, XmlWriteMode.IgnoreSchema, false);
				    xml = sw.ToString();
				}
				
				dh.AddParameter("@GenericUpdateInstructionXml", xml, DbType.Xml);

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Region_Update", cancellationToken);
                wasExecutionSuccessful = executionResult.WasSuccessful;
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			ClearSnapshot();
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will insert/update one new row into the database using the property Information
		/// </summary>
		/// 
        /// <param name="getBackValues" type="bool">Should re-populate values returned from database.</param>
        /// 
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM		Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public virtual bool Upsert(bool getBackValues = false) 
		{

			DatabaseHelper dh = new DatabaseHelper(_databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			var table = new DataTable("Table");
			table.Columns.Add("ColumnName", typeof(string));
			table.Columns.Add("NewValue", typeof(string));

			try
			{
			
			
			
				
				foreach (var (propName, value) in GetChangedPropertiesWithValues())
				{
				    var row = table.NewRow();
				    row["ColumnName"] = propName;
				    row["NewValue"] = value ?? DBNull.Value;
				    table.Rows.Add(row);
				}
				
				string xml;
				using (var sw = new StringWriter())
				{
				    table.WriteXml(sw, XmlWriteMode.IgnoreSchema, false);
				    xml = sw.ToString();
				}
				
				dh.AddParameter("@GenericUpdateInstructionXml", xml, DbType.Xml);

				PopulateDatabaseHelperParameters(dh);
				
			    if (!getBackValues && !dh.ShouldUseBackupServer)
			    {
				     var executionResult = dh.ExecuteScalar("gsp_Region_Upsert");
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server
			    {
				      var executionResult = dh.ExecuteReader("gsp_Region_Upsert");
					  dr = executionResult.Result!;
					  wasExecutionSuccessful = executionResult.WasSuccessful;
				      if (dr.Read())
				      {
							PopulateObjectFromReader(this, dr);
				      }    
					  else
					  {
					  	  TakeSnapshot();
					  }
					  dr.Close();
			    }
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will asynchronously insert/update one new row into the database using the property Information
		/// </summary>
		/// 
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="getBackValues" type="bool">Should re-populate values returned from database.</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM		Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public virtual async Task<bool> UpsertAsync(bool getBackValues = false, CancellationToken cancellationToken = default) 
		{
		
			DatabaseHelper dh = new DatabaseHelper(_databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			var table = new DataTable("Table");
			table.Columns.Add("ColumnName", typeof(string));
			table.Columns.Add("NewValue", typeof(string));
			
			try
			{
			
			
			
				foreach (var (propName, value) in GetChangedPropertiesWithValues())
				{
					var row = table.NewRow();
					row["ColumnName"] = propName;
					row["NewValue"] = value ?? DBNull.Value;
					table.Rows.Add(row);
				}
				string xml;
				using (var sw = new StringWriter())
				{
					table.WriteXml(sw, XmlWriteMode.IgnoreSchema, false);
					xml = sw.ToString();
				}
				dh.AddParameter("@GenericUpdateInstructionXml", xml, DbType.Xml);
				
				PopulateDatabaseHelperParameters(dh);
				
			    if (!getBackValues && !dh.ShouldUseBackupServer)
			    {
				     var executionResult = await dh.ExecuteScalarAsync("gsp_Region_Upsert", cancellationToken);
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server 
			    {
				      var executionResult = await dh.ExecuteReaderAsync("gsp_Region_Upsert", cancellationToken);
					  dr = executionResult.Result!;
					  wasExecutionSuccessful = executionResult.WasSuccessful;
				      if (dr.Read())
				      {
                            PopulateObjectFromReader(this, dr);
				      }
					  else
					  {
						  TakeSnapshot();
					  }
				      dr.Close();                
			    }
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will Delete one row from the database using the property Information
		/// </summary>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public virtual bool Delete() 
		{

            DatabaseHelper dh = new DatabaseHelper(_databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try 
			{
			
			
				// Pass the value of '_regionID' as parameter 'RegionID' of the stored procedure.
				dh.AddParameter("@RegionID", _regionID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
			
                //Try Primary Server
                var executionResult = dh.ExecuteScalar("gsp_Region_Delete");
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will asynchronously Delete one row from the database using the property Information
		/// </summary>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public virtual async Task<bool> DeleteAsync() 
		{

            DatabaseHelper dh = new DatabaseHelper(_databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try 
			{
			
			
				// Pass the value of '_regionID' as parameter 'RegionID' of the stored procedure.
				dh.AddParameter("@RegionID", _regionID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Region_Delete");
                wasExecutionSuccessful = executionResult.WasSuccessful;
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will asynchronously Delete one row from the database using the property Information
		/// </summary>
		///
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public virtual async Task<bool> DeleteAsync(CancellationToken cancellationToken) 
		{

            DatabaseHelper dh = new DatabaseHelper(_databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try 
			{
			
			
				// Pass the value of '_regionID' as parameter 'RegionID' of the stored procedure.
				dh.AddParameter("@RegionID", _regionID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Region_Delete", cancellationToken);
                wasExecutionSuccessful = executionResult.WasSuccessful;
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will Delete one row from the database using the primary key information
		/// </summary>
		///
		/// <param name="pk" type="RegionPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool Delete(RegionPrimaryKey pk, DatabaseHelper? databaseHelper = null) 
		{
			
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try 
			{
			
			
				// Pass the values of all key parameters to the stored procedure.
				System.Collections.Specialized.NameValueCollection nvc = pk.GetKeysAndValues();
				foreach (string nvcKey in nvc.Keys)
				{
					dh.AddParameter("@" + nvcKey, nvc[nvcKey] );
				}
					// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
   				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
   
	
				
                //Try Primary Server
                var executionResult = dh.ExecuteScalar("gsp_Region_Delete");
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will asynchronously Delete one row from the database using the primary key information
		/// </summary>
		///
		/// <param name="pk" type="RegionPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> DeleteAsync(RegionPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default) 
		{
			
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				// Pass the values of all key parameters to the stored procedure.
				System.Collections.Specialized.NameValueCollection nvc = pk.GetKeysAndValues();
				foreach (string nvcKey in nvc.Keys)
				{
					dh.AddParameter("@" + nvcKey, nvc[nvcKey] );
				}
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
   				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
   
		
                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Region_Delete", cancellationToken);
                wasExecutionSuccessful = executionResult.WasSuccessful;
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will Delete row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="RegionFields">Field of the class Region</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool DeleteByField(string field, object fieldValue, DatabaseHelper? databaseHelper = null)
		{
			
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@Field", field);
				dh.AddParameter("@Value", fieldValue);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
			
                //Try Primary Server
                var executionResult = dh.ExecuteScalar("gsp_Region_DeleteByField");
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will asynchronously Delete row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="RegionFields">Field of the class Region</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> DeleteByFieldAsync(string field, object fieldValue, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@Field", field);
				dh.AddParameter("@Value", fieldValue);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
			
                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Region_DeleteByField", cancellationToken);
                wasExecutionSuccessful = executionResult.WasSuccessful;
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will Delete row(s) from the database using the provided filter
		/// </summary>
		///
		/// <param name="queryBuilderFunc" type="Func<IConditionalQueryBuilder, IQuery>">A function that accepts an `IConditionalQueryBuilder` to build a query using the fluent API.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool Delete(Func<IConditionalQueryBuilder, IQuery> queryBuilderFunc, DatabaseHelper? databaseHelper = null)
		{
			
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
				var builder = new ConditionalQueryBuilder();
				queryBuilderFunc(builder);
				string query = builder.Build().ToString();
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@whereClause", query);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
			
                //Try Primary Server
                var executionResult = dh.ExecuteScalar("gsp_Region_DeleteByField");
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				}
				catch (Exception ex)
				{
					dh.ProcessException(ex);
				}
				finally
				{
					dr?.Close();
					dh.Dispose();
				}
				
				return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will asynchronously Delete row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="queryBuilderFunc" type="Func<IConditionalQueryBuilder, IQuery>">A function that accepts an `IConditionalQueryBuilder` to build a query using the fluent API.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> DeleteAsync(Func<IConditionalQueryBuilder, IQuery> queryBuilderFunc, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			
			try
			{
			
				var builder = new ConditionalQueryBuilder();
				queryBuilderFunc(builder);
				string query = builder.Build().ToString();
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@whereClause", query);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
		
                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Region_DeleteByField", cancellationToken);
                wasExecutionSuccessful = executionResult.WasSuccessful;
			
				}
				catch (Exception ex)
				{
					dh.ProcessException(ex);
				}
				finally
				{
					dr?.Close();
					dh.Dispose();
				}
				
				return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will return an object representing the record matching the primary key information specified.
		/// </summary>
		///
		/// <param name="pk" type="RegionPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Region</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Region? SelectOne(RegionPrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				// Pass the values of all key parameters to the stored procedure.
				System.Collections.Specialized.NameValueCollection nvc = pk.GetKeysAndValues();
				foreach (string nvcKey in nvc.Keys)
				{
					dh.AddParameter("@" + nvcKey, nvc[nvcKey] );
				}
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteReader("gsp_Region_SelectByPrimaryKey");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				if (dr.Read())
				{
					Region obj = new Region(databaseHelper);	
					PopulateObjectFromReader(obj, dr);
					dr.Close();
					return obj;
				}
				else
				{
					dr.Close();
					return null;
				}
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return null;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will asynchronously return an object representing the record matching the primary key information specified.
		/// </summary>
		///
		/// <param name="pk" type="RegionPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Region</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Region?> SelectOneAsync(RegionPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				// Pass the values of all key parameters to the stored procedure.
				System.Collections.Specialized.NameValueCollection nvc = pk.GetKeysAndValues();
				foreach (string nvcKey in nvc.Keys)
				{
					dh.AddParameter("@" + nvcKey, nvc[nvcKey] );
				}
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Region_SelectByPrimaryKey", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				if (dr.Read())
				{
					Region obj = new Region(databaseHelper);	
					PopulateObjectFromReader(obj, dr);
					dr.Close();              
					dh.Dispose();
					return obj;
				}
				else
				{
					dr.Close();
					dh.Dispose();
					return null;
				}
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return null;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will return a list of objects representing all records in the table.
		/// </summary>
		///
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>list of objects of class Region in the form of object of Regions </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Regions SelectAll(DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				dh.AddParameter("@whereClause", DBNull.Value);
				dh.AddParameter("@numberOfRecordsToReturn", DBNull.Value);	
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteReader("gsp_Region_SelectAll");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Regions list = PopulateObjectsFromReader(dr, dh);
				foreach (var entity in list)
				{
					entity._databaseHelper = null;
				}
				dr.Close();
				dh.Dispose();
				return list;
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return [];
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will asynchronously return a list of objects representing all records in the table.
		/// </summary>
		///
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>list of objects of class Region in the form of object of Regions </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Regions> SelectAllAsync(DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				dh.AddParameter("@whereClause", DBNull.Value);
				dh.AddParameter("@numberOfRecordsToReturn", DBNull.Value);	
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Region_SelectAll", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Regions list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
				foreach (var entity in list)
				{
					entity._databaseHelper = null;
				}
				dr.Close();
				dh.Dispose();
				return list;
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return [];
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will return a filtered list of objects representing all records in the table.
		/// </summary>
		///
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		/// <param name="queryBuilderFunc" type="Func<IBaseQueryBuilder, IQuery>">A function that accepts an `IBaseQueryBuilder` to build a query using the fluent API.</param>
		/// <param name="numberOfRecordsToReturn" type="int?">Specify the count for the records to be return, default all records.</param>
		///
		/// <returns>list of objects of class Region in the form of object of Regions </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Regions SelectAll(Func<IBaseQueryBuilder, IQuery> queryBuilderFunc, int? numberOfRecordsToReturn = null, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
				var builder = new QueryBuilder();
				queryBuilderFunc(builder);
				string query = builder.Build().ToString();
			
				// Pass the specified field and its value to the stored procedure.
				if (!string.IsNullOrEmpty(query))
					dh.AddParameter("@whereClause", query);
				else
					dh.AddParameter("@whereClause", DBNull.Value);
			
				if (numberOfRecordsToReturn is not null)
					dh.AddParameter("@numberOfRecordsToReturn", numberOfRecordsToReturn);
				else
					dh.AddParameter("@numberOfRecordsToReturn", DBNull.Value);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteReader("gsp_Region_SelectAll");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Regions list = PopulateObjectsFromReader(dr, dh);
				foreach (var entity in list)
				{
					entity._databaseHelper = null;
				}
				dr.Close();
				dh.Dispose();
				return list;
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return [];
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will asynchronously return a filtered list of objects representing all records in the table.
		/// </summary>
		///
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		/// <param name="queryBuilderFunc" type="Func<IBaseQueryBuilder, IQuery>">A function that accepts an `IBaseQueryBuilder` to build a query using the fluent API.</param>
		/// <param name="numberOfRecordsToReturn" type="int?">Specify the count for the records to be return, default all records.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		///
		/// <returns>list of objects of class Region in the form of object of Regions </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Regions> SelectAllAsync(Func<IBaseQueryBuilder, IQuery> queryBuilderFunc, int? numberOfRecordsToReturn = null, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try 
			{
			
				var builder = new QueryBuilder();
				queryBuilderFunc(builder);
				string query = builder.Build().ToString();
			
				// Pass the specified field and its value to the stored procedure.
				if (!string.IsNullOrEmpty(query))
					dh.AddParameter("@whereClause", query);
				else
					dh.AddParameter("@whereClause", DBNull.Value);
			
				if (numberOfRecordsToReturn is not null)
					dh.AddParameter("@numberOfRecordsToReturn", numberOfRecordsToReturn);
				else
					dh.AddParameter("@numberOfRecordsToReturn", DBNull.Value);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Region_SelectAll", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Regions list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
				foreach (var entity in list)
				{
					entity._databaseHelper = null;
				}	
				dr.Close();
				dh.Dispose();
				return list;
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return [];
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// Deprecated. Use SelectByField(string field, object fieldValue, object fieldValue2, TypeOperation typeOperation) instead. This method will get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Region</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Region in the form of an object of class Regions</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Regions SelectByField(string field, object fieldValue, DatabaseHelper? databaseHelper = null)
		{
			return SelectByField(field, fieldValue, null, TypeOperation.Equal, null, null, databaseHelper);
			
		}

		/// <summary>
		/// Deprecated. Use SelectByFieldAsync(string field, object fieldValue, object fieldValue2, TypeOperation typeOperation) instead. This method will asynchronously get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Region</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Region in the form of an object of class Regions</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Regions> SelectByFieldAsync(string field, object fieldValue, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			return await SelectByFieldAsync(field, fieldValue, null, TypeOperation.Equal, null, null, databaseHelper, cancellationToken);
			
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Region</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Region in the form of an object of class Regions</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Regions SelectByField(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, string? orderByField = null, string orderByDirection = "ASC", DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@Field", field);
				dh.AddParameter("@Value", fieldValue);
				if (fieldValue2 is not null)
					dh.AddParameter("@Value2", fieldValue2);
				else
				  dh.AddParameter("@Value2", DBNull.Value);
				dh.AddParameter("@Operation", OperationCollection.Operation[(int)typeOperation] );
			
				// Optional OrderBy field and direction parameters
				if (!string.IsNullOrEmpty(orderByField))
				{{
					dh.AddParameter("@OrderByField", orderByField);
					dh.AddParameter("@OrderByDirection", orderByDirection);
				}}
			
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteReader("gsp_Region_SelectByField");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Regions list = PopulateObjectsFromReader(dr, dh);
				foreach (var entity in list)
				{
					entity._databaseHelper = null;
				}
				dr.Close();
				dh.Dispose();
				return list;
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return [];
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will asynchronously get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Region</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Region in the form of an object of class Regions</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Regions> SelectByFieldAsync(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, string? orderByField = null, string orderByDirection = "ASC", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@Field",field);
				dh.AddParameter("@Value", fieldValue);
				if (fieldValue2 is not null)
					dh.AddParameter("@Value2", fieldValue2);
				else
				  dh.AddParameter("@Value2", DBNull.Value);
				dh.AddParameter("@Operation", OperationCollection.Operation[(int)typeOperation] );
			
				// Optional OrderBy field and direction parameters
				if (!string.IsNullOrEmpty(orderByField))
				{{
					dh.AddParameter("@OrderByField", orderByField);
					dh.AddParameter("@OrderByDirection", orderByDirection);
				}}
			
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Region_SelectByField", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Regions list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
				foreach (var entity in list)
				{
					entity._databaseHelper = null;
				}
				dr.Close();
				dh.Dispose();
				return list;
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return [];
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will return a list of objects representing the specified number of entries from the specified record number in the table.
		/// </summary>
		///
		/// <param name="pageSize" type="int">Number of records returned.</param>
        /// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The method to sort the field on.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>list of objects of class Region in the form of object of Regions </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Regions SelectAllPaged(int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@PageSize", pageSize);
				dh.AddParameter("@PageNumber", pageNumber);
				dh.AddParameter("@OrderByStatement", orderByStatement);
			
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteReader("gsp_Region_SelectAllPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Regions list = PopulateObjectsFromReader(dr, dh);
				foreach (var entity in list)
				{
					entity._databaseHelper = null;
				}
				dr.Close();
				dh.Dispose();
				return list;
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return [];
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will asynchronously return a list of objects representing the specified number of entries from the specified record number in the table.
		/// </summary>
		///
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The method to sort the field on.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>list of objects of class Region in the form of object of Regions </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Regions> SelectAllPagedAsync(int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@PageSize", pageSize);
				dh.AddParameter("@PageNumber", pageNumber);
				dh.AddParameter("@OrderByStatement", orderByStatement);
			
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Region_SelectAllPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Regions list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
				foreach (var entity in list)
				{
					entity._databaseHelper = null;
				}
				dr.Close();
				dh.Dispose();
				return list;
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return [];
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will return a list of objects representing the specified number of entries from the specified record number in the table 
		/// using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Region</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="orderByStatement" type="string">The field value to number.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Region in the form of an object of class Regions</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Regions SelectByFieldPaged(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@Field", field);
				dh.AddParameter("@Value", fieldValue);
				if (fieldValue2 is not null)
					dh.AddParameter("@Value2", fieldValue2);
				else
				  dh.AddParameter("@Value2", DBNull.Value);
				dh.AddParameter("@Operation", OperationCollection.Operation[(int)typeOperation]);
				dh.AddParameter("@PageSize", pageSize);
				dh.AddParameter("@PageNumber", pageNumber);
				dh.AddParameter("@OrderByStatement", orderByStatement);
			
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteReader("gsp_Region_SelectByFieldPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Regions list = PopulateObjectsFromReader(dr, dh);
				foreach (var entity in list)
				{
					entity._databaseHelper = null;
				}
				dr.Close();
				dh.Dispose();
				return list;
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return [];
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will asynchronously return a list of objects representing the specified number of entries from the specified record number in the table 
		/// using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Region</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="orderByStatement" type="string">The field value to number.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Region in the form of an object of class Regions</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Regions> SelectByFieldPagedAsync(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@Field", field);
				dh.AddParameter("@Value", fieldValue);
				if (fieldValue2 is not null)
					dh.AddParameter("@Value2", fieldValue2);
				else
				  dh.AddParameter("@Value2", DBNull.Value);
				dh.AddParameter("@Operation", OperationCollection.Operation[(int)typeOperation]);
				dh.AddParameter("@PageSize", pageSize);
				dh.AddParameter("@PageNumber", pageNumber);
				dh.AddParameter("@OrderByStatement", orderByStatement);
			
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Region_SelectByFieldPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Regions list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
				foreach (var entity in list)
				{
					entity._databaseHelper = null;
				}
				dr.Close();
				dh.Dispose();
				return list;
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return [];
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will return a count all records in the table.
		/// </summary>
		///
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>count records</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static int SelectAllCount(DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);		
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
	// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteScalar("gsp_Region_SelectAllCount");
				var obj = executionResult.Result;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				dh.Dispose();
				
				return Convert.ToInt32(obj);
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return 0;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will asynchronously return a count all records in the table.
		/// </summary>
		///
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>count records</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<int> SelectAllCountAsync(DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);	
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
			
	// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Region_SelectAllCount", cancellationToken);
				var obj = executionResult.Result;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				dh.Dispose();
						
			
                return Convert.ToInt32(obj);
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return 0;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will return a count all records in the table based on provided filder.
		/// </summary>
		///
		/// <param name="queryBuilderFunc" type="Func<IConditionalQueryBuilder, IQuery>">A function that accepts an `IConditionalQueryBuilder` to build a query using the fluent API.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>count records</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static int SelectAllCount(Func<IConditionalQueryBuilder, IQuery> queryBuilderFunc, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			
			try
			{
			
				var builder = new ConditionalQueryBuilder();
				queryBuilderFunc(builder);
				string query = builder.Build().ToString();
			
				// Pass the specified field and its value to the stored procedure.
				if (!string.IsNullOrEmpty(query))
					dh.AddParameter("@whereClause", query);
				else
					dh.AddParameter("@whereClause", DBNull.Value);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteScalar("gsp_Region_SelectAllCount");
				var obj = executionResult.Result;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				dh.Dispose();
			
                return Convert.ToInt32(obj);
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return 0;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will asynchronously return a count all records in the table based on provided filder.
		/// </summary>
		///
		/// <param name="queryBuilderFunc" type="Func<IConditionalQueryBuilder, IQuery>">A function that accepts an `IConditionalQueryBuilder` to build a query using the fluent API.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>count records</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<int> SelectAllCountAsync(Func<IConditionalQueryBuilder, IQuery> queryBuilderFunc, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);		
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
			try
			{
			
				var builder = new ConditionalQueryBuilder();
				queryBuilderFunc(builder);
				string query = builder.Build().ToString();
			
				// Pass the specified field and its value to the stored procedure.
				if (!string.IsNullOrEmpty(query))
					dh.AddParameter("@whereClause", query);
				else
					dh.AddParameter("@whereClause", DBNull.Value);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Region_SelectAllCount", cancellationToken);
				var obj = executionResult.Result;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				dh.Dispose();
						
			
                return Convert.ToInt32(obj);
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return 0;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will check if a row exists in the table using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="RegionFields">Field of the class Region</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool ExistsByField(string field, object fieldValue, DatabaseHelper? databaseHelper = null)
		{
			
			bool wasExecutionSuccessful = false;
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			
			try
			{
			
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@Field", field);
				dh.AddParameter("@Value", fieldValue);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteScalar("gsp_Region_ExistsByField");
				var obj = executionResult.Result;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				dh.Dispose();
			
			
                return Convert.ToBoolean(obj);
				
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return false;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will asynchronously check if a row exists in the table using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="RegionFields">Field of the class Region</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> ExistsByFieldAsync(string field, object fieldValue, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			
			try
			{
			
			
				// Pass the specified field and its value to the stored procedure.
				dh.AddParameter("@Field", field);
				dh.AddParameter("@Value", fieldValue);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

				var executionResult = await dh.ExecuteScalarAsync("gsp_Region_ExistsByField", cancellationToken);
				var obj = executionResult.Result;
				var wasExecutionSuccessful = executionResult.WasSuccessful;
				dh.Dispose();
			
			
                return Convert.ToBoolean(obj);
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return false;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will check if a row exists in the table using the value of the primary key
		/// </summary>
		///
		/// <param name="pk" type="RegionPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool Exists(RegionPrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			
			bool wasExecutionSuccessful = false;
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			
			try
			{
			
			
				// Pass the values of all key parameters to the stored procedure.
				System.Collections.Specialized.NameValueCollection nvc = pk.GetKeysAndValues();
				foreach (string nvcKey in nvc.Keys)
				{
					if (nvc[nvcKey] is null)
						dh.AddParameter("@" + nvcKey, DBNull.Value);
					else 
						dh.AddParameter("@" + nvcKey, nvc[nvcKey]);
				}
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteScalar("gsp_Region_ExistsByPrimaryKey");
				var obj = executionResult.Result;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				dh.Dispose();
			
			
                return Convert.ToBoolean(obj);
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return false;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will asynchronously check if a row exists in the table using the value of the primary key
		/// </summary>
		///
		/// <param name="pk" type="RegionPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> ExistsAsync(RegionPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			
			try
			{
			
			
				// Pass the values of all key parameters to the stored procedure.
				System.Collections.Specialized.NameValueCollection nvc = pk.GetKeysAndValues();
				foreach (string nvcKey in nvc.Keys)
				{
					if (nvc[nvcKey] is null)
						dh.AddParameter("@" + nvcKey, DBNull.Value);
					else 
						dh.AddParameter("@" + nvcKey, nvc[nvcKey]);
				}
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Region_ExistsByPrimaryKey", cancellationToken);
				var obj = executionResult.Result;
				var wasExecutionSuccessful = executionResult.WasSuccessful;
				dh.Dispose();
			
			
                return Convert.ToBoolean(obj);
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return false;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will check if a row exists in the table using the provided filter
		/// </summary>
		///
		/// <param name="queryBuilderFunc" type="Func<IConditionalQueryBuilder, IQuery>">A function that accepts an `IConditionalQueryBuilder` to build a query using the fluent API.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool Exists(Func<IConditionalQueryBuilder, IQuery> queryBuilderFunc, DatabaseHelper? databaseHelper = null)
		{
			
			bool wasExecutionSuccessful = false;
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			
			
			try
			{
			
				var builder = new ConditionalQueryBuilder();
				queryBuilderFunc(builder);
				string query = builder.Build().ToString();
			
				// Pass the values of parameters to the stored procedure.
				dh.AddParameter("@whereClause", query);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteScalar("gsp_Region_ExistsByField");
				var obj = executionResult.Result;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				dh.Dispose();
			
			
                return Convert.ToBoolean(obj);
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return false;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will asynchronously check if a row exists in the table using the value of the primary key
		/// </summary>
		///
		/// <param name="queryBuilderFunc" type="Func<IConditionalQueryBuilder, IQuery>">A function that accepts an `IConditionalQueryBuilder` to build a query using the fluent API.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> ExistsAsync(Func<IConditionalQueryBuilder, IQuery> queryBuilderFunc, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			
			
			try
			{
			
				var builder = new ConditionalQueryBuilder();
				queryBuilderFunc(builder);
				string query = builder.Build().ToString();
			
				// Pass the values of parameters to the stored procedure.
				dh.AddParameter("@whereClause", query);
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Region_ExistsByField", cancellationToken);
				var obj = executionResult.Result;
				var wasExecutionSuccessful = executionResult.WasSuccessful;
				dh.Dispose();
			
			
                return Convert.ToBoolean(obj);
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
				return false;
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
		}

		/// <summary>
		/// This method will return an object representing the record matching the primary key information specified.
		/// </summary>
		///
		/// <param name="regionID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Region</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Region? SelectOne(int regionID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new RegionPrimaryKey(regionID);
			return SelectOne(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously return an object representing the record matching the primary key information specified.
		/// </summary>
		///
		/// <param name="regionID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Region</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Region?> SelectOneAsync(int regionID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new RegionPrimaryKey(regionID);
			return await SelectOneAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will Delete one row from the database using the primary key information
		/// </summary>
		///
		/// <param name="regionID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool Delete(int regionID, DatabaseHelper? databaseHelper = null) 
		{
			var pk = new RegionPrimaryKey(regionID);
			return Delete(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously Delete one row from the database using the primary key information
		/// </summary>
		///
		/// <param name="regionID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> DeleteAsync(int regionID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default) 
		{
			var pk = new RegionPrimaryKey(regionID);
			return await DeleteAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will check if a row exists in the table using the value of the primary key
		/// </summary>
		///
		/// <param name="regionID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool Exists(int regionID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new RegionPrimaryKey(regionID);
			return Exists(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously check if a row exists in the table using the value of the primary key
		/// </summary>
		///
		/// <param name="regionID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> ExistsAsync(int regionID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new RegionPrimaryKey(regionID);
			return await ExistsAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="RegionPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Region</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Region? SelectOneWithTerritoriesUsingRegionID(RegionPrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Region? obj = null;
			
			try
			{
			
			
				// Pass the values of all key parameters to the stored procedure.
				System.Collections.Specialized.NameValueCollection nvc = pk.GetKeysAndValues();
				foreach (string nvcKey in nvc.Keys)
				{
					dh.AddParameter("@" + nvcKey,nvc[nvcKey] );			
				}
			
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteReader("gsp_Region_SelectOneWithTerritoriesUsingRegionID");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (dr.Read())
					{
						obj = new Region(databaseHelper);
						PopulateObjectFromReader(obj,dr);
				
						dr.NextResult();
				
						//Get the child records.
						obj.TerritoriesUsingRegionID = Territory.PopulateObjectsFromReader(dr, databaseHelper);
					}
				dr.Close();
				dh.Dispose();
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
			return obj;
			
		}

		/// <summary>
		/// This method will asynchronously get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="RegionPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Region</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Region?> SelectOneWithTerritoriesUsingRegionIDAsync(RegionPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Region? obj = null;
			
			try
			{
			
			
				// Pass the values of all key parameters to the stored procedure.
				System.Collections.Specialized.NameValueCollection nvc = pk.GetKeysAndValues();
				foreach (string nvcKey in nvc.Keys)
				{
					dh.AddParameter("@" + nvcKey,nvc[nvcKey] );			
				}
			
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Region_SelectOneWithTerritoriesUsingRegionID", cancellationToken);			
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (await dr.ReadAsync(cancellationToken))
				{
					obj = new Region(databaseHelper);
					PopulateObjectFromReader(obj,dr);
				
					dr.NextResult();
				
					//Get the child records.
					obj.TerritoriesUsingRegionID = await Territory.PopulateObjectsFromReaderAsync(dr, databaseHelper, cancellationToken);
				}
				dr.Close();
				dh.Dispose();
				
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dr?.Close();
				dh.Dispose();
			}
			
			return obj;
			
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="regionID" type="RegionPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Region</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Region? SelectOneWithTerritoriesUsingRegionID(int regionID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new RegionPrimaryKey(regionID);
			return SelectOneWithTerritoriesUsingRegionID(pk, databaseHelper);
		}
		/// <summary>
		/// This method will asynchronously get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="regionID" type="RegionPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Region</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Region?> SelectOneWithTerritoriesUsingRegionIDAsync(int regionID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new RegionPrimaryKey(regionID);
			return await SelectOneWithTerritoriesUsingRegionIDAsync(pk, databaseHelper, cancellationToken);
		}
		#endregion	
		
		#region Methods (Private)
		
		/// <summary>
		/// Populates the fields of a single objects from the columns found in an open reader.
		/// </summary>
		/// <param name="obj" type="Region">Object of Region to populate</param>
		/// <param name="rdr" type="IDataReader">An object that implements the IDataReader interface</param>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static void PopulateObjectFromReader(RegionBase obj,IDataReader rdr) 
		{

			int ord_RegionID = rdr.GetOrdinal(RegionFields.RegionID);
			int ord_RegionDescription = rdr.GetOrdinal(RegionFields.RegionDescription);

			obj.RegionID = rdr.GetInt32(ord_RegionID);
			obj.RegionDescription = rdr.GetString(ord_RegionDescription);

			obj.TakeSnapshot();
		}

		/// <summary>
		/// Populates the fields for multiple objects from the columns found in an open reader.
		/// </summary>
		///
		/// <param name="rdr" type="IDataReader">An object that implements the IDataReader interface</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>Object of Regions</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Regions PopulateObjectsFromReader(IDataReader rdr, DatabaseHelper? databaseHelper)
		{
			Regions list = new Regions();
			
			while (rdr.Read())
			{
				Region obj = new Region(databaseHelper);
				PopulateObjectFromReader(obj,rdr);
				list.Add(obj);
			}
			return list;
			
		}

		/// <summary>
		/// Populates the fields for multiple objects from the columns found in an open reader.
		/// </summary>
		///
		/// <param name="rdr" type="DbDataReader">An object that implements the IDataReader interface</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		/// <param name="cancellationToken" type="CancellationToken">For cancelling the operation</param>
		///
		/// <returns>Object of Regions</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Regions> PopulateObjectsFromReaderAsync(DbDataReader rdr, DatabaseHelper? databaseHelper, CancellationToken cancellationToken)
		{
			Regions list = new Regions();
			
			while (await rdr.ReadAsync(cancellationToken))
			{
				Region obj = new Region(databaseHelper);
				PopulateObjectFromReader(obj,rdr);
				list.Add(obj);
			}
			return list;
			
		}

		/// <summary>
		/// Populates the fields for multiple objects from the columns found in an open reader.
		/// </summary>
		///
		/// <param name="rdr" type="IDataReader">An object that implements the IDataReader interface</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>Object of Regions</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		internal static Regions PopulateObjectsFromReaderWithCheckingReader(IDataReader rdr, DatabaseHelper databaseHelper) 
		{

			Regions list = new Regions();
			
            if (rdr.Read())
			{
				Region obj = new Region(databaseHelper);
				PopulateObjectFromReader(obj, rdr);
				list.Add(obj);
				while (rdr.Read())
				{
					obj = new Region(databaseHelper);
					PopulateObjectFromReader(obj, rdr);
					list.Add(obj);
				}
				return list;
			}
			else
			{
				return list;
			}
			
		}

		/// <summary>
		/// Populates the fields for multiple objects from the columns found in an open reader.
		/// </summary>
		///
		/// <param name="rdr" type="IDataReader">An object that implements the IDataReader interface</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>Object of Regions</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		internal static async Task<Regions> PopulateObjectsFromReaderWithCheckingReaderAsync(DbDataReader rdr, DatabaseHelper databaseHelper, CancellationToken cancellationToken) 
		{

			Regions list = new Regions();
			
            if (await rdr.ReadAsync(cancellationToken))
			{
				Region obj = new Region(databaseHelper);
				PopulateObjectFromReader(obj, rdr);
				list.Add(obj);
				while (await rdr.ReadAsync(cancellationToken))
				{
					obj = new Region(databaseHelper);
					PopulateObjectFromReader(obj, rdr);
					list.Add(obj);
				}
				return list;
			}
			else
			{
				return list;
			}
			
		}

		/// <summary>
		/// Populates the parameters for the Region table stored procedures.
		/// </summary>
		///
		/// <param name="dh" type="DatabaseHelper">DatabaseHelper to populate parameters on</param>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		internal void PopulateDatabaseHelperParameters(DatabaseHelper dh)
		{
			// Pass the value of '_regionID' as parameter 'RegionID' of the stored procedure.
			dh.AddParameter("@RegionID", _regionID);

			// Pass the value of '_regionDescription' as parameter 'RegionDescription' of the stored procedure.
			dh.AddParameter("@RegionDescription", _regionDescription);

			// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
			dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

		}

	
	#endregion

	}
}
