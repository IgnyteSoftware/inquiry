//
// Class	:	CategoryBase.cs
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
	public partial class CategoryFields
	{
		public const string CategoryID                = "CategoryID";
		public const string CategoryName              = "CategoryName";
		public const string Description               = "Description";
		public const string Picture                   = "Picture";
	}
	
	/// <summary>
	/// Data access class for the "Categories" table.
	/// </summary>
	[Serializable]
	public class CategoryBase : TrackableEntity<CategoryBase>
	{
		
		#region Class Level Variables
		
		private DatabaseHelper? _databaseHelper = null;
    
		private int            	_categoryID              	= 0;
		private int            ?	_originalCategoryID      	= 0;
		private string         	_categoryName            	= string.Empty;
		private string?        	_description             	= null;
		private byte[]?        	_picture                 	= null;

		private Products? _productsCategoryID = null;
		
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
		public CategoryBase(DatabaseHelper? databaseHelper = null) { 
                _databaseHelper = databaseHelper;
                TakeSnapshot();
          }
					
		#endregion
		
		#region Properties

		
		/// <summary>
		/// Returns the identifier of the persistent object. Don't set it manually!
		/// </summary>
		[Trackable]
		public int CategoryID
		{
			get 
			{ 
				return _categoryID;
			}
			set 
			{
			
				if (_originalCategoryID is null || !_originalCategoryID.HasValue)
						_originalCategoryID = _categoryID;
				_categoryID = value; 
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "CategoryName" field. Length must be between 0 and 40 characters. Mandatory.
		/// </summary>
		[Trackable]
		public string CategoryName
		{
			get 
			{ 
				return _categoryName.Trim();
			}
			set 
			{
				
				if (value is null)
					throw new ArgumentNullException("value", "Value is null.");
				
				if (value is not null && value.Length > 40)
					throw new ArgumentException("CategoryName length must be between 0 and 40 characters.");
				
				if (value is not null)
				{		           
					_categoryName = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Description" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? Description
		{
			get 
			{ 
				if (_description is null) return _description;
				return _description.Trim(); 
			}
			set 
			{
				if (value is null)
					_description = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("Description length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_description = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Picture" field.  If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public byte[]? Picture
		{
			get 
			{ 
				return _picture;
			}
			set 
			{
			
				_picture = value; 
			}
		}
      

		/// <summary>
		/// Provides access to the related table 'Products'
		/// </summary>
		public Products? ProductsUsingCategoryID
		{
			get 
			{
				if (_productsCategoryID is null)
				{
					_productsCategoryID = new Products();
					_productsCategoryID = Product.SelectAllByForeignKeyCategoryID(new CategoryPrimaryKey(CategoryID), _databaseHelper);
				}                
				return _productsCategoryID; 
			}
			set 
			{
				  _productsCategoryID = value;
			}
		}		
		//This property is related to the table name that exist in database
		
		public static string TableName
		{
			get 
			{ 
				  return "Categories";
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
					var executionResult = dh.ExecuteScalar("gsp_Categories_Insert");
					wasExecutionSuccessful = executionResult.WasSuccessful;
					if (wasExecutionSuccessful)
                    {
                        TakeSnapshot();
                    }
				}
				else //Try Primary Server
				{
					var executionResult = dh.ExecuteReader("gsp_Categories_Insert");
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
				     var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_Insert", cancellationToken);
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server 
			    {
				      var executionResult = await dh.ExecuteReaderAsync("gsp_Categories_Insert", cancellationToken);
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
			
			
				// Pass the value of '_categoryID' as parameter 'CategoryID' of the stored procedure.
				dh.AddParameter("@CategoryID", _originalCategoryID.OrIfNullOrEmpty(_categoryID));
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
                var executionResult = dh.ExecuteScalar("gsp_Categories_Update");
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
			
			
				// Pass the value of '_categoryID' as parameter 'CategoryID' of the stored procedure.
				dh.AddParameter("@CategoryID", _originalCategoryID.OrIfNullOrEmpty(_categoryID));
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_Update");
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
			
			
				// Pass the value of '_categoryID' as parameter 'CategoryID' of the stored procedure.
				dh.AddParameter("@CategoryID", _originalCategoryID.OrIfNullOrEmpty(_categoryID));
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_Update", cancellationToken);
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
				     var executionResult = dh.ExecuteScalar("gsp_Categories_Upsert");
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server
			    {
				      var executionResult = dh.ExecuteReader("gsp_Categories_Upsert");
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
				     var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_Upsert", cancellationToken);
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server 
			    {
				      var executionResult = await dh.ExecuteReaderAsync("gsp_Categories_Upsert", cancellationToken);
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
			
			
				// Pass the value of '_categoryID' as parameter 'CategoryID' of the stored procedure.
				dh.AddParameter("@CategoryID", _categoryID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
			
                //Try Primary Server
                var executionResult = dh.ExecuteScalar("gsp_Categories_Delete");
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
			
			
				// Pass the value of '_categoryID' as parameter 'CategoryID' of the stored procedure.
				dh.AddParameter("@CategoryID", _categoryID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_Delete");
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
			
			
				// Pass the value of '_categoryID' as parameter 'CategoryID' of the stored procedure.
				dh.AddParameter("@CategoryID", _categoryID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_Delete", cancellationToken);
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
		/// <param name="pk" type="CategoryPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static bool Delete(CategoryPrimaryKey pk, DatabaseHelper? databaseHelper = null) 
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
                var executionResult = dh.ExecuteScalar("gsp_Categories_Delete");
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
		/// <param name="pk" type="CategoryPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static async Task<bool> DeleteAsync(CategoryPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default) 
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_Delete", cancellationToken);
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
		/// <param name="field" type="CategoryFields">Field of the class Category</param>
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
                var executionResult = dh.ExecuteScalar("gsp_Categories_DeleteByField");
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
		/// <param name="field" type="CategoryFields">Field of the class Category</param>
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_DeleteByField", cancellationToken);
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
                var executionResult = dh.ExecuteScalar("gsp_Categories_DeleteByField");
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_DeleteByField", cancellationToken);
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
		/// <param name="pk" type="CategoryPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Category</returns>
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
		public static Category? SelectOne(CategoryPrimaryKey pk, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Categories_SelectByPrimaryKey");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				if (dr.Read())
				{
					Category obj = new Category(databaseHelper);	
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
		/// <param name="pk" type="CategoryPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Category</returns>
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
		public static async Task<Category?> SelectOneAsync(CategoryPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Categories_SelectByPrimaryKey", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				if (dr.Read())
				{
					Category obj = new Category(databaseHelper);	
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
		/// <returns>list of objects of class Category in the form of object of Categories </returns>
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
		public static Categories SelectAll(DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Categories_SelectAll");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Categories list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class Category in the form of object of Categories </returns>
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
		public static async Task<Categories> SelectAllAsync(DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Categories_SelectAll", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Categories list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <returns>list of objects of class Category in the form of object of Categories </returns>
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
		public static Categories SelectAll(Func<IBaseQueryBuilder, IQuery> queryBuilderFunc, int? numberOfRecordsToReturn = null, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Categories_SelectAll");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Categories list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class Category in the form of object of Categories </returns>
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
		public static async Task<Categories> SelectAllAsync(Func<IBaseQueryBuilder, IQuery> queryBuilderFunc, int? numberOfRecordsToReturn = null, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Categories_SelectAll", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Categories list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <param name="field" type="string">Field of the class Category</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Category in the form of an object of class Categories</returns>
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
		public static Categories SelectByField(string field, object fieldValue, DatabaseHelper? databaseHelper = null)
		{
			return SelectByField(field, fieldValue, null, TypeOperation.Equal, null, null, databaseHelper);
			
		}

		/// <summary>
		/// Deprecated. Use SelectByFieldAsync(string field, object fieldValue, object fieldValue2, TypeOperation typeOperation) instead. This method will asynchronously get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Category</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Category in the form of an object of class Categories</returns>
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
		public static async Task<Categories> SelectByFieldAsync(string field, object fieldValue, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			return await SelectByFieldAsync(field, fieldValue, null, TypeOperation.Equal, null, null, databaseHelper, cancellationToken);
			
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Category</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Category in the form of an object of class Categories</returns>
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
		public static Categories SelectByField(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, string? orderByField = null, string orderByDirection = "ASC", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Categories_SelectByField");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Categories list = PopulateObjectsFromReader(dr, dh);
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
		/// <param name="field" type="string">Field of the class Category</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Category in the form of an object of class Categories</returns>
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
		public static async Task<Categories> SelectByFieldAsync(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, string? orderByField = null, string orderByDirection = "ASC", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Categories_SelectByField", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Categories list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <returns>list of objects of class Category in the form of object of Categories </returns>
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
		public static Categories SelectAllPaged(int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Categories_SelectAllPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Categories list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class Category in the form of object of Categories </returns>
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
		public static async Task<Categories> SelectAllPagedAsync(int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Categories_SelectAllPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Categories list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <param name="field" type="string">Field of the class Category</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="orderByStatement" type="string">The field value to number.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Category in the form of an object of class Categories</returns>
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
		public static Categories SelectByFieldPaged(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Categories_SelectByFieldPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Categories list = PopulateObjectsFromReader(dr, dh);
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
		/// <param name="field" type="string">Field of the class Category</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="orderByStatement" type="string">The field value to number.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Category in the form of an object of class Categories</returns>
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
		public static async Task<Categories> SelectByFieldPagedAsync(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Categories_SelectByFieldPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Categories list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
			
				var executionResult = dh.ExecuteScalar("gsp_Categories_SelectAllCount");
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_SelectAllCount", cancellationToken);
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
			
				var executionResult = dh.ExecuteScalar("gsp_Categories_SelectAllCount");
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_SelectAllCount", cancellationToken);
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
		/// <param name="field" type="CategoryFields">Field of the class Category</param>
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
			
				var executionResult = dh.ExecuteScalar("gsp_Categories_ExistsByField");
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
		/// <param name="field" type="CategoryFields">Field of the class Category</param>
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
			

				var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_ExistsByField", cancellationToken);
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
		/// <param name="pk" type="CategoryPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static bool Exists(CategoryPrimaryKey pk, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteScalar("gsp_Categories_ExistsByPrimaryKey");
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
		/// <param name="pk" type="CategoryPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static async Task<bool> ExistsAsync(CategoryPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_ExistsByPrimaryKey", cancellationToken);
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
			
				var executionResult = dh.ExecuteScalar("gsp_Categories_ExistsByField");
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Categories_ExistsByField", cancellationToken);
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
		/// <param name="categoryID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Category</returns>
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
		public static Category? SelectOne(int categoryID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new CategoryPrimaryKey(categoryID);
			return SelectOne(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously return an object representing the record matching the primary key information specified.
		/// </summary>
		///
		/// <param name="categoryID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Category</returns>
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
		public static async Task<Category?> SelectOneAsync(int categoryID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new CategoryPrimaryKey(categoryID);
			return await SelectOneAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will Delete one row from the database using the primary key information
		/// </summary>
		///
		/// <param name="categoryID" type="int">Primary Key information based on which data is to be fetched.</param>
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
		public static bool Delete(int categoryID, DatabaseHelper? databaseHelper = null) 
		{
			var pk = new CategoryPrimaryKey(categoryID);
			return Delete(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously Delete one row from the database using the primary key information
		/// </summary>
		///
		/// <param name="categoryID" type="int">Primary Key information based on which data is to be fetched.</param>
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
		public static async Task<bool> DeleteAsync(int categoryID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default) 
		{
			var pk = new CategoryPrimaryKey(categoryID);
			return await DeleteAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will check if a row exists in the table using the value of the primary key
		/// </summary>
		///
		/// <param name="categoryID" type="int">Primary Key information based on which data is to be fetched.</param>
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
		public static bool Exists(int categoryID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new CategoryPrimaryKey(categoryID);
			return Exists(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously check if a row exists in the table using the value of the primary key
		/// </summary>
		///
		/// <param name="categoryID" type="int">Primary Key information based on which data is to be fetched.</param>
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
		public static async Task<bool> ExistsAsync(int categoryID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new CategoryPrimaryKey(categoryID);
			return await ExistsAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="CategoriesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Categories</returns>
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
		public static Category? SelectOneWithProductsUsingCategoryID(CategoryPrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Category? obj = null;
			
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
			
				var executionResult = dh.ExecuteReader("gsp_Categories_SelectOneWithProductsUsingCategoryID");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (dr.Read())
					{
						obj = new Category(databaseHelper);
						PopulateObjectFromReader(obj,dr);
				
						dr.NextResult();
				
						//Get the child records.
						obj.ProductsUsingCategoryID = Product.PopulateObjectsFromReader(dr, databaseHelper);
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
		/// <param name="pk" type="CategoriesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Categories</returns>
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
		public static async Task<Category?> SelectOneWithProductsUsingCategoryIDAsync(CategoryPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Category? obj = null;
			
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Categories_SelectOneWithProductsUsingCategoryID", cancellationToken);			
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (await dr.ReadAsync(cancellationToken))
				{
					obj = new Category(databaseHelper);
					PopulateObjectFromReader(obj,dr);
				
					dr.NextResult();
				
					//Get the child records.
					obj.ProductsUsingCategoryID = await Product.PopulateObjectsFromReaderAsync(dr, databaseHelper, cancellationToken);
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
		/// <param name="categoryID" type="CategoriesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Categories</returns>
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
		public static Category? SelectOneWithProductsUsingCategoryID(int categoryID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new CategoryPrimaryKey(categoryID);
			return SelectOneWithProductsUsingCategoryID(pk, databaseHelper);
		}
		/// <summary>
		/// This method will asynchronously get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="categoryID" type="CategoriesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Categories</returns>
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
		public static async Task<Category?> SelectOneWithProductsUsingCategoryIDAsync(int categoryID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new CategoryPrimaryKey(categoryID);
			return await SelectOneWithProductsUsingCategoryIDAsync(pk, databaseHelper, cancellationToken);
		}
		#endregion	
		
		#region Methods (Private)
		
		/// <summary>
		/// Populates the fields of a single objects from the columns found in an open reader.
		/// </summary>
		/// <param name="obj" type="Categories">Object of Categories to populate</param>
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
		public static void PopulateObjectFromReader(CategoryBase obj,IDataReader rdr) 
		{

			int ord_CategoryID = rdr.GetOrdinal(CategoryFields.CategoryID);
			int ord_CategoryName = rdr.GetOrdinal(CategoryFields.CategoryName);
			int ord_Description = rdr.GetOrdinal(CategoryFields.Description);
			int ord_Picture = rdr.GetOrdinal(CategoryFields.Picture);

			obj.CategoryID = rdr.GetInt32(ord_CategoryID);
			obj.CategoryName = rdr.GetString(ord_CategoryName);
			if (!rdr.IsDBNull(ord_Description))
			{
				obj.Description = rdr.GetString(ord_Description);
			}
			
			if (!rdr.IsDBNull(ord_Picture))
			{
				obj.Picture = (System.Byte[])rdr.GetValue(ord_Picture);
			}
			

			obj.TakeSnapshot();
		}

		/// <summary>
		/// Populates the fields for multiple objects from the columns found in an open reader.
		/// </summary>
		///
		/// <param name="rdr" type="IDataReader">An object that implements the IDataReader interface</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>Object of Categories</returns>
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
		public static Categories PopulateObjectsFromReader(IDataReader rdr, DatabaseHelper? databaseHelper)
		{
			Categories list = new Categories();
			
			while (rdr.Read())
			{
				Category obj = new Category(databaseHelper);
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
		/// <returns>Object of Categories</returns>
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
		public static async Task<Categories> PopulateObjectsFromReaderAsync(DbDataReader rdr, DatabaseHelper? databaseHelper, CancellationToken cancellationToken)
		{
			Categories list = new Categories();
			
			while (await rdr.ReadAsync(cancellationToken))
			{
				Category obj = new Category(databaseHelper);
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
		/// <returns>Object of Categories</returns>
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
		internal static Categories PopulateObjectsFromReaderWithCheckingReader(IDataReader rdr, DatabaseHelper databaseHelper) 
		{

			Categories list = new Categories();
			
            if (rdr.Read())
			{
				Category obj = new Category(databaseHelper);
				PopulateObjectFromReader(obj, rdr);
				list.Add(obj);
				while (rdr.Read())
				{
					obj = new Category(databaseHelper);
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
		/// <returns>Object of Categories</returns>
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
		internal static async Task<Categories> PopulateObjectsFromReaderWithCheckingReaderAsync(DbDataReader rdr, DatabaseHelper databaseHelper, CancellationToken cancellationToken) 
		{

			Categories list = new Categories();
			
            if (await rdr.ReadAsync(cancellationToken))
			{
				Category obj = new Category(databaseHelper);
				PopulateObjectFromReader(obj, rdr);
				list.Add(obj);
				while (await rdr.ReadAsync(cancellationToken))
				{
					obj = new Category(databaseHelper);
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
		/// Populates the parameters for the Categories table stored procedures.
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
			// Pass the value of '_categoryID' as parameter 'CategoryID' of the stored procedure.
			dh.AddParameter("@CategoryID", _categoryID);

			// Pass the value of '_categoryName' as parameter 'CategoryName' of the stored procedure.
			dh.AddParameter("@CategoryName", _categoryName);

			// Pass the value of '_description' as parameter 'Description' of the stored procedure.
			if(_description is not null)
			  dh.AddParameter("@Description", _description);
			else
			  dh.AddParameter("@Description", DBNull.Value );

			// Pass the value of '_picture' as parameter 'Picture' of the stored procedure.
			if(_picture is not null)
			  dh.AddParameter("@Picture", _picture , DbType.Binary );
			else
			  dh.AddParameter("@Picture", DBNull.Value , DbType.Binary );
			  
			// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
			dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

		}

	
	#endregion

	}
}
