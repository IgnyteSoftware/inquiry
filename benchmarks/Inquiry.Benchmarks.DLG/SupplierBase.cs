//
// Class	:	SupplierBase.cs
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
	public partial class SupplierFields
	{
		public const string SupplierID                = "SupplierID";
		public const string CompanyName               = "CompanyName";
		public const string ContactName               = "ContactName";
		public const string ContactTitle              = "ContactTitle";
		public const string Address                   = "Address";
		public const string City                      = "City";
		public const string Region                    = "Region";
		public const string PostalCode                = "PostalCode";
		public const string Country                   = "Country";
		public const string Phone                     = "Phone";
		public const string Fax                       = "Fax";
		public const string HomePage                  = "HomePage";
	}
	
	/// <summary>
	/// Data access class for the "Suppliers" table.
	/// </summary>
	[Serializable]
	public class SupplierBase : TrackableEntity<SupplierBase>
	{
		
		#region Class Level Variables
		
		private DatabaseHelper? _databaseHelper = null;
    
		private int            	_supplierID              	= 0;
		private int            ?	_originalSupplierID      	= 0;
		private string         	_companyName             	= string.Empty;
		private string?        	_contactName             	= null;
		private string?        	_contactTitle            	= null;
		private string?        	_address                 	= null;
		private string?        	_city                    	= null;
		private string?        	_region                  	= null;
		private string?        	_postalCode              	= null;
		private string?        	_country                 	= null;
		private string?        	_phone                   	= null;
		private string?        	_fax                     	= null;
		private string?        	_homePage                	= null;

		private Products? _productsSupplierID = null;
		
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
		public SupplierBase(DatabaseHelper? databaseHelper = null) { 
                _databaseHelper = databaseHelper;
                TakeSnapshot();
          }
					
		#endregion
		
		#region Properties

		
		/// <summary>
		/// Returns the identifier of the persistent object. Don't set it manually!
		/// </summary>
		[Trackable]
		public int SupplierID
		{
			get 
			{ 
				return _supplierID;
			}
			set 
			{
			
				if (_originalSupplierID is null || !_originalSupplierID.HasValue)
						_originalSupplierID = _supplierID;
				_supplierID = value; 
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "CompanyName" field. Length must be between 0 and 40 characters. Mandatory.
		/// </summary>
		[Trackable]
		public string CompanyName
		{
			get 
			{ 
				return _companyName.Trim();
			}
			set 
			{
				
				if (value is null)
					throw new ArgumentNullException("value", "Value is null.");
				
				if (value is not null && value.Length > 40)
					throw new ArgumentException("CompanyName length must be between 0 and 40 characters.");
				
				if (value is not null)
				{		           
					_companyName = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "ContactName" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? ContactName
		{
			get 
			{ 
				if (_contactName is null) return _contactName;
				return _contactName.Trim(); 
			}
			set 
			{
				if (value is null)
					_contactName = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("ContactName length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_contactName = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "ContactTitle" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? ContactTitle
		{
			get 
			{ 
				if (_contactTitle is null) return _contactTitle;
				return _contactTitle.Trim(); 
			}
			set 
			{
				if (value is null)
					_contactTitle = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("ContactTitle length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_contactTitle = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Address" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? Address
		{
			get 
			{ 
				if (_address is null) return _address;
				return _address.Trim(); 
			}
			set 
			{
				if (value is null)
					_address = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("Address length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_address = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "City" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? City
		{
			get 
			{ 
				if (_city is null) return _city;
				return _city.Trim(); 
			}
			set 
			{
				if (value is null)
					_city = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("City length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_city = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Region" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? Region
		{
			get 
			{ 
				if (_region is null) return _region;
				return _region.Trim(); 
			}
			set 
			{
				if (value is null)
					_region = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("Region length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_region = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "PostalCode" field. Length must be between 0 and 20 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? PostalCode
		{
			get 
			{ 
				if (_postalCode is null) return _postalCode;
				return _postalCode.Trim(); 
			}
			set 
			{
				if (value is null)
					_postalCode = value;
				
				
				if (value is not null && value.Length > 20)
					throw new ArgumentException("PostalCode length must be between 0 and 20 characters.");
				
				if (value is not null)
				{		           
					_postalCode = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Country" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? Country
		{
			get 
			{ 
				if (_country is null) return _country;
				return _country.Trim(); 
			}
			set 
			{
				if (value is null)
					_country = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("Country length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_country = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Phone" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? Phone
		{
			get 
			{ 
				if (_phone is null) return _phone;
				return _phone.Trim(); 
			}
			set 
			{
				if (value is null)
					_phone = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("Phone length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_phone = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Fax" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? Fax
		{
			get 
			{ 
				if (_fax is null) return _fax;
				return _fax.Trim(); 
			}
			set 
			{
				if (value is null)
					_fax = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("Fax length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_fax = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "HomePage" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? HomePage
		{
			get 
			{ 
				if (_homePage is null) return _homePage;
				return _homePage.Trim(); 
			}
			set 
			{
				if (value is null)
					_homePage = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("HomePage length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_homePage = value.Trim(); 
				}
			}
		}
      

		/// <summary>
		/// Provides access to the related table 'Products'
		/// </summary>
		public Products? ProductsUsingSupplierID
		{
			get 
			{
				if (_productsSupplierID is null)
				{
					_productsSupplierID = new Products();
					_productsSupplierID = Product.SelectAllByForeignKeySupplierID(new SupplierPrimaryKey(SupplierID), _databaseHelper);
				}                
				return _productsSupplierID; 
			}
			set 
			{
				  _productsSupplierID = value;
			}
		}		
		//This property is related to the table name that exist in database
		
		public static string TableName
		{
			get 
			{ 
				  return "Suppliers";
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
					var executionResult = dh.ExecuteScalar("gsp_Suppliers_Insert");
					wasExecutionSuccessful = executionResult.WasSuccessful;
					if (wasExecutionSuccessful)
                    {
                        TakeSnapshot();
                    }
				}
				else //Try Primary Server
				{
					var executionResult = dh.ExecuteReader("gsp_Suppliers_Insert");
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
				     var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_Insert", cancellationToken);
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server 
			    {
				      var executionResult = await dh.ExecuteReaderAsync("gsp_Suppliers_Insert", cancellationToken);
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
			
			
				// Pass the value of '_supplierID' as parameter 'SupplierID' of the stored procedure.
				dh.AddParameter("@SupplierID", _originalSupplierID.OrIfNullOrEmpty(_supplierID));
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
                var executionResult = dh.ExecuteScalar("gsp_Suppliers_Update");
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
			
			
				// Pass the value of '_supplierID' as parameter 'SupplierID' of the stored procedure.
				dh.AddParameter("@SupplierID", _originalSupplierID.OrIfNullOrEmpty(_supplierID));
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_Update");
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
			
			
				// Pass the value of '_supplierID' as parameter 'SupplierID' of the stored procedure.
				dh.AddParameter("@SupplierID", _originalSupplierID.OrIfNullOrEmpty(_supplierID));
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_Update", cancellationToken);
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
				     var executionResult = dh.ExecuteScalar("gsp_Suppliers_Upsert");
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server
			    {
				      var executionResult = dh.ExecuteReader("gsp_Suppliers_Upsert");
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
				     var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_Upsert", cancellationToken);
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server 
			    {
				      var executionResult = await dh.ExecuteReaderAsync("gsp_Suppliers_Upsert", cancellationToken);
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
			
			
				// Pass the value of '_supplierID' as parameter 'SupplierID' of the stored procedure.
				dh.AddParameter("@SupplierID", _supplierID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
			
                //Try Primary Server
                var executionResult = dh.ExecuteScalar("gsp_Suppliers_Delete");
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
			
			
				// Pass the value of '_supplierID' as parameter 'SupplierID' of the stored procedure.
				dh.AddParameter("@SupplierID", _supplierID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_Delete");
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
			
			
				// Pass the value of '_supplierID' as parameter 'SupplierID' of the stored procedure.
				dh.AddParameter("@SupplierID", _supplierID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_Delete", cancellationToken);
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
		/// <param name="pk" type="SupplierPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static bool Delete(SupplierPrimaryKey pk, DatabaseHelper? databaseHelper = null) 
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
                var executionResult = dh.ExecuteScalar("gsp_Suppliers_Delete");
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
		/// <param name="pk" type="SupplierPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static async Task<bool> DeleteAsync(SupplierPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default) 
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_Delete", cancellationToken);
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
		/// <param name="field" type="SupplierFields">Field of the class Supplier</param>
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
                var executionResult = dh.ExecuteScalar("gsp_Suppliers_DeleteByField");
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
		/// <param name="field" type="SupplierFields">Field of the class Supplier</param>
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_DeleteByField", cancellationToken);
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
                var executionResult = dh.ExecuteScalar("gsp_Suppliers_DeleteByField");
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_DeleteByField", cancellationToken);
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
		/// <param name="pk" type="SupplierPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Supplier</returns>
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
		public static Supplier? SelectOne(SupplierPrimaryKey pk, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Suppliers_SelectByPrimaryKey");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				if (dr.Read())
				{
					Supplier obj = new Supplier(databaseHelper);	
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
		/// <param name="pk" type="SupplierPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Supplier</returns>
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
		public static async Task<Supplier?> SelectOneAsync(SupplierPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Suppliers_SelectByPrimaryKey", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				if (dr.Read())
				{
					Supplier obj = new Supplier(databaseHelper);	
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
		/// <returns>list of objects of class Supplier in the form of object of Suppliers </returns>
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
		public static Suppliers SelectAll(DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Suppliers_SelectAll");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Suppliers list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class Supplier in the form of object of Suppliers </returns>
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
		public static async Task<Suppliers> SelectAllAsync(DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Suppliers_SelectAll", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Suppliers list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <returns>list of objects of class Supplier in the form of object of Suppliers </returns>
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
		public static Suppliers SelectAll(Func<IBaseQueryBuilder, IQuery> queryBuilderFunc, int? numberOfRecordsToReturn = null, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Suppliers_SelectAll");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Suppliers list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class Supplier in the form of object of Suppliers </returns>
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
		public static async Task<Suppliers> SelectAllAsync(Func<IBaseQueryBuilder, IQuery> queryBuilderFunc, int? numberOfRecordsToReturn = null, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Suppliers_SelectAll", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Suppliers list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <param name="field" type="string">Field of the class Supplier</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Supplier in the form of an object of class Suppliers</returns>
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
		public static Suppliers SelectByField(string field, object fieldValue, DatabaseHelper? databaseHelper = null)
		{
			return SelectByField(field, fieldValue, null, TypeOperation.Equal, null, null, databaseHelper);
			
		}

		/// <summary>
		/// Deprecated. Use SelectByFieldAsync(string field, object fieldValue, object fieldValue2, TypeOperation typeOperation) instead. This method will asynchronously get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Supplier</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Supplier in the form of an object of class Suppliers</returns>
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
		public static async Task<Suppliers> SelectByFieldAsync(string field, object fieldValue, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			return await SelectByFieldAsync(field, fieldValue, null, TypeOperation.Equal, null, null, databaseHelper, cancellationToken);
			
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Supplier</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Supplier in the form of an object of class Suppliers</returns>
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
		public static Suppliers SelectByField(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, string? orderByField = null, string orderByDirection = "ASC", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Suppliers_SelectByField");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Suppliers list = PopulateObjectsFromReader(dr, dh);
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
		/// <param name="field" type="string">Field of the class Supplier</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Supplier in the form of an object of class Suppliers</returns>
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
		public static async Task<Suppliers> SelectByFieldAsync(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, string? orderByField = null, string orderByDirection = "ASC", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Suppliers_SelectByField", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Suppliers list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <returns>list of objects of class Supplier in the form of object of Suppliers </returns>
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
		public static Suppliers SelectAllPaged(int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Suppliers_SelectAllPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Suppliers list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class Supplier in the form of object of Suppliers </returns>
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
		public static async Task<Suppliers> SelectAllPagedAsync(int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Suppliers_SelectAllPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Suppliers list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <param name="field" type="string">Field of the class Supplier</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="orderByStatement" type="string">The field value to number.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Supplier in the form of an object of class Suppliers</returns>
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
		public static Suppliers SelectByFieldPaged(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Suppliers_SelectByFieldPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Suppliers list = PopulateObjectsFromReader(dr, dh);
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
		/// <param name="field" type="string">Field of the class Supplier</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="orderByStatement" type="string">The field value to number.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Supplier in the form of an object of class Suppliers</returns>
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
		public static async Task<Suppliers> SelectByFieldPagedAsync(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Suppliers_SelectByFieldPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Suppliers list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
			
				var executionResult = dh.ExecuteScalar("gsp_Suppliers_SelectAllCount");
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_SelectAllCount", cancellationToken);
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
			
				var executionResult = dh.ExecuteScalar("gsp_Suppliers_SelectAllCount");
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_SelectAllCount", cancellationToken);
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
		/// <param name="field" type="SupplierFields">Field of the class Supplier</param>
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
			
				var executionResult = dh.ExecuteScalar("gsp_Suppliers_ExistsByField");
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
		/// <param name="field" type="SupplierFields">Field of the class Supplier</param>
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
			

				var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_ExistsByField", cancellationToken);
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
		/// <param name="pk" type="SupplierPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static bool Exists(SupplierPrimaryKey pk, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteScalar("gsp_Suppliers_ExistsByPrimaryKey");
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
		/// <param name="pk" type="SupplierPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static async Task<bool> ExistsAsync(SupplierPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_ExistsByPrimaryKey", cancellationToken);
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
			
				var executionResult = dh.ExecuteScalar("gsp_Suppliers_ExistsByField");
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Suppliers_ExistsByField", cancellationToken);
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
		/// <param name="supplierID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Supplier</returns>
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
		public static Supplier? SelectOne(int supplierID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new SupplierPrimaryKey(supplierID);
			return SelectOne(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously return an object representing the record matching the primary key information specified.
		/// </summary>
		///
		/// <param name="supplierID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Supplier</returns>
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
		public static async Task<Supplier?> SelectOneAsync(int supplierID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new SupplierPrimaryKey(supplierID);
			return await SelectOneAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will Delete one row from the database using the primary key information
		/// </summary>
		///
		/// <param name="supplierID" type="int">Primary Key information based on which data is to be fetched.</param>
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
		public static bool Delete(int supplierID, DatabaseHelper? databaseHelper = null) 
		{
			var pk = new SupplierPrimaryKey(supplierID);
			return Delete(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously Delete one row from the database using the primary key information
		/// </summary>
		///
		/// <param name="supplierID" type="int">Primary Key information based on which data is to be fetched.</param>
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
		public static async Task<bool> DeleteAsync(int supplierID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default) 
		{
			var pk = new SupplierPrimaryKey(supplierID);
			return await DeleteAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will check if a row exists in the table using the value of the primary key
		/// </summary>
		///
		/// <param name="supplierID" type="int">Primary Key information based on which data is to be fetched.</param>
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
		public static bool Exists(int supplierID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new SupplierPrimaryKey(supplierID);
			return Exists(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously check if a row exists in the table using the value of the primary key
		/// </summary>
		///
		/// <param name="supplierID" type="int">Primary Key information based on which data is to be fetched.</param>
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
		public static async Task<bool> ExistsAsync(int supplierID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new SupplierPrimaryKey(supplierID);
			return await ExistsAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="SuppliersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Suppliers</returns>
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
		public static Supplier? SelectOneWithProductsUsingSupplierID(SupplierPrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Supplier? obj = null;
			
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
			
				var executionResult = dh.ExecuteReader("gsp_Suppliers_SelectOneWithProductsUsingSupplierID");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (dr.Read())
					{
						obj = new Supplier(databaseHelper);
						PopulateObjectFromReader(obj,dr);
				
						dr.NextResult();
				
						//Get the child records.
						obj.ProductsUsingSupplierID = Product.PopulateObjectsFromReader(dr, databaseHelper);
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
		/// <param name="pk" type="SuppliersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Suppliers</returns>
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
		public static async Task<Supplier?> SelectOneWithProductsUsingSupplierIDAsync(SupplierPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Supplier? obj = null;
			
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Suppliers_SelectOneWithProductsUsingSupplierID", cancellationToken);			
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (await dr.ReadAsync(cancellationToken))
				{
					obj = new Supplier(databaseHelper);
					PopulateObjectFromReader(obj,dr);
				
					dr.NextResult();
				
					//Get the child records.
					obj.ProductsUsingSupplierID = await Product.PopulateObjectsFromReaderAsync(dr, databaseHelper, cancellationToken);
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
		/// <param name="supplierID" type="SuppliersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Suppliers</returns>
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
		public static Supplier? SelectOneWithProductsUsingSupplierID(int supplierID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new SupplierPrimaryKey(supplierID);
			return SelectOneWithProductsUsingSupplierID(pk, databaseHelper);
		}
		/// <summary>
		/// This method will asynchronously get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="supplierID" type="SuppliersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Suppliers</returns>
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
		public static async Task<Supplier?> SelectOneWithProductsUsingSupplierIDAsync(int supplierID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new SupplierPrimaryKey(supplierID);
			return await SelectOneWithProductsUsingSupplierIDAsync(pk, databaseHelper, cancellationToken);
		}
		#endregion	
		
		#region Methods (Private)
		
		/// <summary>
		/// Populates the fields of a single objects from the columns found in an open reader.
		/// </summary>
		/// <param name="obj" type="Suppliers">Object of Suppliers to populate</param>
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
		public static void PopulateObjectFromReader(SupplierBase obj,IDataReader rdr) 
		{

			int ord_SupplierID = rdr.GetOrdinal(SupplierFields.SupplierID);
			int ord_CompanyName = rdr.GetOrdinal(SupplierFields.CompanyName);
			int ord_ContactName = rdr.GetOrdinal(SupplierFields.ContactName);
			int ord_ContactTitle = rdr.GetOrdinal(SupplierFields.ContactTitle);
			int ord_Address = rdr.GetOrdinal(SupplierFields.Address);
			int ord_City = rdr.GetOrdinal(SupplierFields.City);
			int ord_Region = rdr.GetOrdinal(SupplierFields.Region);
			int ord_PostalCode = rdr.GetOrdinal(SupplierFields.PostalCode);
			int ord_Country = rdr.GetOrdinal(SupplierFields.Country);
			int ord_Phone = rdr.GetOrdinal(SupplierFields.Phone);
			int ord_Fax = rdr.GetOrdinal(SupplierFields.Fax);
			int ord_HomePage = rdr.GetOrdinal(SupplierFields.HomePage);

			obj.SupplierID = rdr.GetInt32(ord_SupplierID);
			obj.CompanyName = rdr.GetString(ord_CompanyName);
			if (!rdr.IsDBNull(ord_ContactName))
			{
				obj.ContactName = rdr.GetString(ord_ContactName);
			}
			
			if (!rdr.IsDBNull(ord_ContactTitle))
			{
				obj.ContactTitle = rdr.GetString(ord_ContactTitle);
			}
			
			if (!rdr.IsDBNull(ord_Address))
			{
				obj.Address = rdr.GetString(ord_Address);
			}
			
			if (!rdr.IsDBNull(ord_City))
			{
				obj.City = rdr.GetString(ord_City);
			}
			
			if (!rdr.IsDBNull(ord_Region))
			{
				obj.Region = rdr.GetString(ord_Region);
			}
			
			if (!rdr.IsDBNull(ord_PostalCode))
			{
				obj.PostalCode = rdr.GetString(ord_PostalCode);
			}
			
			if (!rdr.IsDBNull(ord_Country))
			{
				obj.Country = rdr.GetString(ord_Country);
			}
			
			if (!rdr.IsDBNull(ord_Phone))
			{
				obj.Phone = rdr.GetString(ord_Phone);
			}
			
			if (!rdr.IsDBNull(ord_Fax))
			{
				obj.Fax = rdr.GetString(ord_Fax);
			}
			
			if (!rdr.IsDBNull(ord_HomePage))
			{
				obj.HomePage = rdr.GetString(ord_HomePage);
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
		/// <returns>Object of Suppliers</returns>
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
		public static Suppliers PopulateObjectsFromReader(IDataReader rdr, DatabaseHelper? databaseHelper)
		{
			Suppliers list = new Suppliers();
			
			while (rdr.Read())
			{
				Supplier obj = new Supplier(databaseHelper);
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
		/// <returns>Object of Suppliers</returns>
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
		public static async Task<Suppliers> PopulateObjectsFromReaderAsync(DbDataReader rdr, DatabaseHelper? databaseHelper, CancellationToken cancellationToken)
		{
			Suppliers list = new Suppliers();
			
			while (await rdr.ReadAsync(cancellationToken))
			{
				Supplier obj = new Supplier(databaseHelper);
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
		/// <returns>Object of Suppliers</returns>
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
		internal static Suppliers PopulateObjectsFromReaderWithCheckingReader(IDataReader rdr, DatabaseHelper databaseHelper) 
		{

			Suppliers list = new Suppliers();
			
            if (rdr.Read())
			{
				Supplier obj = new Supplier(databaseHelper);
				PopulateObjectFromReader(obj, rdr);
				list.Add(obj);
				while (rdr.Read())
				{
					obj = new Supplier(databaseHelper);
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
		/// <returns>Object of Suppliers</returns>
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
		internal static async Task<Suppliers> PopulateObjectsFromReaderWithCheckingReaderAsync(DbDataReader rdr, DatabaseHelper databaseHelper, CancellationToken cancellationToken) 
		{

			Suppliers list = new Suppliers();
			
            if (await rdr.ReadAsync(cancellationToken))
			{
				Supplier obj = new Supplier(databaseHelper);
				PopulateObjectFromReader(obj, rdr);
				list.Add(obj);
				while (await rdr.ReadAsync(cancellationToken))
				{
					obj = new Supplier(databaseHelper);
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
		/// Populates the parameters for the Suppliers table stored procedures.
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
			// Pass the value of '_supplierID' as parameter 'SupplierID' of the stored procedure.
			dh.AddParameter("@SupplierID", _supplierID);

			// Pass the value of '_companyName' as parameter 'CompanyName' of the stored procedure.
			dh.AddParameter("@CompanyName", _companyName);

			// Pass the value of '_contactName' as parameter 'ContactName' of the stored procedure.
			if(_contactName is not null)
			  dh.AddParameter("@ContactName", _contactName);
			else
			  dh.AddParameter("@ContactName", DBNull.Value );

			// Pass the value of '_contactTitle' as parameter 'ContactTitle' of the stored procedure.
			if(_contactTitle is not null)
			  dh.AddParameter("@ContactTitle", _contactTitle);
			else
			  dh.AddParameter("@ContactTitle", DBNull.Value );

			// Pass the value of '_address' as parameter 'Address' of the stored procedure.
			if(_address is not null)
			  dh.AddParameter("@Address", _address);
			else
			  dh.AddParameter("@Address", DBNull.Value );

			// Pass the value of '_city' as parameter 'City' of the stored procedure.
			if(_city is not null)
			  dh.AddParameter("@City", _city);
			else
			  dh.AddParameter("@City", DBNull.Value );

			// Pass the value of '_region' as parameter 'Region' of the stored procedure.
			if(_region is not null)
			  dh.AddParameter("@Region", _region);
			else
			  dh.AddParameter("@Region", DBNull.Value );

			// Pass the value of '_postalCode' as parameter 'PostalCode' of the stored procedure.
			if(_postalCode is not null)
			  dh.AddParameter("@PostalCode", _postalCode);
			else
			  dh.AddParameter("@PostalCode", DBNull.Value );

			// Pass the value of '_country' as parameter 'Country' of the stored procedure.
			if(_country is not null)
			  dh.AddParameter("@Country", _country);
			else
			  dh.AddParameter("@Country", DBNull.Value );

			// Pass the value of '_phone' as parameter 'Phone' of the stored procedure.
			if(_phone is not null)
			  dh.AddParameter("@Phone", _phone);
			else
			  dh.AddParameter("@Phone", DBNull.Value );

			// Pass the value of '_fax' as parameter 'Fax' of the stored procedure.
			if(_fax is not null)
			  dh.AddParameter("@Fax", _fax);
			else
			  dh.AddParameter("@Fax", DBNull.Value );

			// Pass the value of '_homePage' as parameter 'HomePage' of the stored procedure.
			if(_homePage is not null)
			  dh.AddParameter("@HomePage", _homePage);
			else
			  dh.AddParameter("@HomePage", DBNull.Value );

			// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
			dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

		}

	
	#endregion

	}
}
