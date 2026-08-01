//
// Class	:	EmployeeBase.cs
// Author	:  	Inquiry © 2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:12 PM
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
	public partial class EmployeeFields
	{
		public const string EmployeeID                = "EmployeeID";
		public const string LastName                  = "LastName";
		public const string FirstName                 = "FirstName";
		public const string Title                     = "Title";
		public const string TitleOfCourtesy           = "TitleOfCourtesy";
		public const string BirthDate                 = "BirthDate";
		public const string HireDate                  = "HireDate";
		public const string Address                   = "Address";
		public const string City                      = "City";
		public const string Region                    = "Region";
		public const string PostalCode                = "PostalCode";
		public const string Country                   = "Country";
		public const string HomePhone                 = "HomePhone";
		public const string Extension                 = "Extension";
		public const string Photo                     = "Photo";
		public const string Notes                     = "Notes";
		public const string ReportsTo                 = "ReportsTo";
		public const string PhotoPath                 = "PhotoPath";
	}
	
	/// <summary>
	/// Data access class for the "Employees" table.
	/// </summary>
	[Serializable]
	public class EmployeeBase : TrackableEntity<EmployeeBase>
	{
		
		#region Class Level Variables
		
		private DatabaseHelper? _databaseHelper = null;
    
		private int            	_employeeID              	= 0;
		private int            ?	_originalEmployeeID      	= 0;
		private string         	_lastName                	= string.Empty;
		private string         	_firstName               	= string.Empty;
		private string?        	_title                   	= null;
		private string?        	_titleOfCourtesy         	= null;
		private DateTime?      	_birthDate               	= null;
		private DateTime?      	_hireDate                	= null;
		private string?        	_address                 	= null;
		private string?        	_city                    	= null;
		private string?        	_region                  	= null;
		private string?        	_postalCode              	= null;
		private string?        	_country                 	= null;
		private string?        	_homePhone               	= null;
		private string?        	_extension               	= null;
		private byte[]?        	_photo                   	= null;
		private string?        	_notes                   	= null;
		private int?           	_reportsTo               	= null;
		private string?        	_photoPath               	= null;

		private Employees? _employeesReportsTo = null;
		private EmployeeTerritories? _employeeTerritoriesEmployeeID = null;
		private Orders? _ordersEmployeeID = null;
		
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
		public EmployeeBase(DatabaseHelper? databaseHelper = null) { 
                _databaseHelper = databaseHelper;
                TakeSnapshot();
          }
					
		#endregion
		
		#region Properties

		
		/// <summary>
		/// Returns the identifier of the persistent object. Don't set it manually!
		/// </summary>
		[Trackable]
		public int EmployeeID
		{
			get 
			{ 
				return _employeeID;
			}
			set 
			{
			
				if (_originalEmployeeID is null || !_originalEmployeeID.HasValue)
						_originalEmployeeID = _employeeID;
				_employeeID = value; 
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "LastName" field. Length must be between 0 and 40 characters. Mandatory.
		/// </summary>
		[Trackable]
		public string LastName
		{
			get 
			{ 
				return _lastName.Trim();
			}
			set 
			{
				
				if (value is null)
					throw new ArgumentNullException("value", "Value is null.");
				
				if (value is not null && value.Length > 40)
					throw new ArgumentException("LastName length must be between 0 and 40 characters.");
				
				if (value is not null)
				{		           
					_lastName = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "FirstName" field. Length must be between 0 and 40 characters. Mandatory.
		/// </summary>
		[Trackable]
		public string FirstName
		{
			get 
			{ 
				return _firstName.Trim();
			}
			set 
			{
				
				if (value is null)
					throw new ArgumentNullException("value", "Value is null.");
				
				if (value is not null && value.Length > 40)
					throw new ArgumentException("FirstName length must be between 0 and 40 characters.");
				
				if (value is not null)
				{		           
					_firstName = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Title" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? Title
		{
			get 
			{ 
				if (_title is null) return _title;
				return _title.Trim(); 
			}
			set 
			{
				if (value is null)
					_title = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("Title length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_title = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "TitleOfCourtesy" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? TitleOfCourtesy
		{
			get 
			{ 
				if (_titleOfCourtesy is null) return _titleOfCourtesy;
				return _titleOfCourtesy.Trim(); 
			}
			set 
			{
				if (value is null)
					_titleOfCourtesy = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("TitleOfCourtesy length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_titleOfCourtesy = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "BirthDate" field.  If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public DateTime? BirthDate
		{
			get 
			{ 
				return _birthDate;
			}
			set 
			{
			
				_birthDate = value; 
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "HireDate" field.  If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public DateTime? HireDate
		{
			get 
			{ 
				return _hireDate;
			}
			set 
			{
			
				_hireDate = value; 
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
		/// This property is mapped to the "HomePhone" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? HomePhone
		{
			get 
			{ 
				if (_homePhone is null) return _homePhone;
				return _homePhone.Trim(); 
			}
			set 
			{
				if (value is null)
					_homePhone = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("HomePhone length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_homePhone = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Extension" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? Extension
		{
			get 
			{ 
				if (_extension is null) return _extension;
				return _extension.Trim(); 
			}
			set 
			{
				if (value is null)
					_extension = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("Extension length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_extension = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Photo" field.  If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public byte[]? Photo
		{
			get 
			{ 
				return _photo;
			}
			set 
			{
			
				_photo = value; 
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Notes" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? Notes
		{
			get 
			{ 
				if (_notes is null) return _notes;
				return _notes.Trim(); 
			}
			set 
			{
				if (value is null)
					_notes = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("Notes length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_notes = value.Trim(); 
				}
			}
		}
      

		
		/// <summary>
		/// The foreign key connected with another persistent object. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public int? ReportsTo
		{
			get 
			{ 
				return _reportsTo;
			}
			set 
			{
			
				_reportsTo = value; 
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "PhotoPath" field. Length must be between 0 and 2147483647 characters. If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public string? PhotoPath
		{
			get 
			{ 
				if (_photoPath is null) return _photoPath;
				return _photoPath.Trim(); 
			}
			set 
			{
				if (value is null)
					_photoPath = value;
				
				
				if (value is not null && value.Length > 2147483647)
					throw new ArgumentException("PhotoPath length must be between 0 and 2147483647 characters.");
				
				if (value is not null)
				{		           
					_photoPath = value.Trim(); 
				}
			}
		}
      

		/// <summary>
		/// Provides access to the related table 'Employees'
		/// </summary>
		public Employees? EmployeesUsingReportsTo
		{
			get 
			{
				if (_employeesReportsTo is null)
				{
					_employeesReportsTo = new Employees();
					_employeesReportsTo = Employee.SelectAllByForeignKeyReportsTo(new EmployeePrimaryKey(EmployeeID), _databaseHelper);
				}                
				return _employeesReportsTo; 
			}
			set 
			{
				  _employeesReportsTo = value;
			}
		}

		/// <summary>
		/// Provides access to the related table 'EmployeeTerritories'
		/// </summary>
		public EmployeeTerritories? EmployeeTerritoriesUsingEmployeeID
		{
			get 
			{
				if (_employeeTerritoriesEmployeeID is null)
				{
					_employeeTerritoriesEmployeeID = new EmployeeTerritories();
					_employeeTerritoriesEmployeeID = EmployeeTerritory.SelectAllByForeignKeyEmployeeID(new EmployeePrimaryKey(EmployeeID), _databaseHelper);
				}                
				return _employeeTerritoriesEmployeeID; 
			}
			set 
			{
				  _employeeTerritoriesEmployeeID = value;
			}
		}

		/// <summary>
		/// Provides access to the related table 'Orders'
		/// </summary>
		public Orders? OrdersUsingEmployeeID
		{
			get 
			{
				if (_ordersEmployeeID is null)
				{
					_ordersEmployeeID = new Orders();
					_ordersEmployeeID = Order.SelectAllByForeignKeyEmployeeID(new EmployeePrimaryKey(EmployeeID), _databaseHelper);
				}                
				return _ordersEmployeeID; 
			}
			set 
			{
				  _ordersEmployeeID = value;
			}
		}		
		//This property is related to the table name that exist in database
		
		public static string TableName
		{
			get 
			{ 
				  return "Employees";
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
					var executionResult = dh.ExecuteScalar("gsp_Employees_Insert");
					wasExecutionSuccessful = executionResult.WasSuccessful;
					if (wasExecutionSuccessful)
                    {
                        TakeSnapshot();
                    }
				}
				else //Try Primary Server
				{
					var executionResult = dh.ExecuteReader("gsp_Employees_Insert");
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
				     var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_Insert", cancellationToken);
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server 
			    {
				      var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_Insert", cancellationToken);
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
			
				// Pass the value of '_employeeID' as parameter 'EmployeeID' of the stored procedure.
				dh.AddParameter("@EmployeeID", _originalEmployeeID.OrIfNullOrEmpty(_employeeID));
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
                var executionResult = dh.ExecuteScalar("gsp_Employees_Update");
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
			
				// Pass the value of '_employeeID' as parameter 'EmployeeID' of the stored procedure.
				dh.AddParameter("@EmployeeID", _originalEmployeeID.OrIfNullOrEmpty(_employeeID));
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_Update");
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
			
				// Pass the value of '_employeeID' as parameter 'EmployeeID' of the stored procedure.
				dh.AddParameter("@EmployeeID", _originalEmployeeID.OrIfNullOrEmpty(_employeeID));
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_Update", cancellationToken);
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
		/// DLGenerator			06/04/2026 10:07:12 PM		Created function
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
				     var executionResult = dh.ExecuteScalar("gsp_Employees_Upsert");
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server
			    {
				      var executionResult = dh.ExecuteReader("gsp_Employees_Upsert");
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
		/// DLGenerator			06/04/2026 10:07:12 PM		Created function
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
				     var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_Upsert", cancellationToken);
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server 
			    {
				      var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_Upsert", cancellationToken);
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
			
				// Pass the value of '_employeeID' as parameter 'EmployeeID' of the stored procedure.
				dh.AddParameter("@EmployeeID", _employeeID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
			
                //Try Primary Server
                var executionResult = dh.ExecuteScalar("gsp_Employees_Delete");
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
			
				// Pass the value of '_employeeID' as parameter 'EmployeeID' of the stored procedure.
				dh.AddParameter("@EmployeeID", _employeeID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_Delete");
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
			
				// Pass the value of '_employeeID' as parameter 'EmployeeID' of the stored procedure.
				dh.AddParameter("@EmployeeID", _employeeID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_Delete", cancellationToken);
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
		/// <param name="pk" type="EmployeePrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool Delete(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null) 
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
                var executionResult = dh.ExecuteScalar("gsp_Employees_Delete");
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
		/// <param name="pk" type="EmployeePrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> DeleteAsync(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default) 
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_Delete", cancellationToken);
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
		/// <param name="field" type="EmployeeFields">Field of the class Employee</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
                var executionResult = dh.ExecuteScalar("gsp_Employees_DeleteByField");
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
		/// <param name="field" type="EmployeeFields">Field of the class Employee</param>
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_DeleteByField", cancellationToken);
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
                var executionResult = dh.ExecuteScalar("gsp_Employees_DeleteByField");
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_DeleteByField", cancellationToken);
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
		/// <param name="pk" type="EmployeePrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employee</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employee? SelectOne(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectByPrimaryKey");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				if (dr.Read())
				{
					Employee obj = new Employee(databaseHelper);	
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
		/// <param name="pk" type="EmployeePrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employee</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employee?> SelectOneAsync(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectByPrimaryKey", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				if (dr.Read())
				{
					Employee obj = new Employee(databaseHelper);	
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
		/// <returns>list of objects of class Employee in the form of object of Employees </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employees SelectAll(DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectAll");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Employees list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class Employee in the form of object of Employees </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employees> SelectAllAsync(DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectAll", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Employees list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <returns>list of objects of class Employee in the form of object of Employees </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employees SelectAll(Func<IBaseQueryBuilder, IQuery> queryBuilderFunc, int? numberOfRecordsToReturn = null, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectAll");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Employees list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class Employee in the form of object of Employees </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employees> SelectAllAsync(Func<IBaseQueryBuilder, IQuery> queryBuilderFunc, int? numberOfRecordsToReturn = null, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectAll", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Employees list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <param name="field" type="string">Field of the class Employee</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Employee in the form of an object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employees SelectByField(string field, object fieldValue, DatabaseHelper? databaseHelper = null)
		{
			return SelectByField(field, fieldValue, null, TypeOperation.Equal, null, null, databaseHelper);
			
		}

		/// <summary>
		/// Deprecated. Use SelectByFieldAsync(string field, object fieldValue, object fieldValue2, TypeOperation typeOperation) instead. This method will asynchronously get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Employee</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Employee in the form of an object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employees> SelectByFieldAsync(string field, object fieldValue, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			return await SelectByFieldAsync(field, fieldValue, null, TypeOperation.Equal, null, null, databaseHelper, cancellationToken);
			
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class Employee</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Employee in the form of an object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employees SelectByField(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, string? orderByField = null, string orderByDirection = "ASC", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectByField");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				Employees list = PopulateObjectsFromReader(dr, dh);
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
		/// <param name="field" type="string">Field of the class Employee</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Employee in the form of an object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employees> SelectByFieldAsync(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, string? orderByField = null, string orderByDirection = "ASC", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectByField", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Employees list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <returns>list of objects of class Employee in the form of object of Employees </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employees SelectAllPaged(int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectAllPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Employees list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class Employee in the form of object of Employees </returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employees> SelectAllPagedAsync(int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectAllPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Employees list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <param name="field" type="string">Field of the class Employee</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="orderByStatement" type="string">The field value to number.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Employee in the form of an object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employees SelectByFieldPaged(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectByFieldPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Employees list = PopulateObjectsFromReader(dr, dh);
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
		/// <param name="field" type="string">Field of the class Employee</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="orderByStatement" type="string">The field value to number.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class Employee in the form of an object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employees> SelectByFieldPagedAsync(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectByFieldPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				Employees list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
				var executionResult = dh.ExecuteScalar("gsp_Employees_SelectAllCount");
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_SelectAllCount", cancellationToken);
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
				var executionResult = dh.ExecuteScalar("gsp_Employees_SelectAllCount");
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_SelectAllCount", cancellationToken);
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
		/// <param name="field" type="EmployeeFields">Field of the class Employee</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
				var executionResult = dh.ExecuteScalar("gsp_Employees_ExistsByField");
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
		/// <param name="field" type="EmployeeFields">Field of the class Employee</param>
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			

				var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_ExistsByField", cancellationToken);
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
		/// <param name="pk" type="EmployeePrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool Exists(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteScalar("gsp_Employees_ExistsByPrimaryKey");
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
		/// <param name="pk" type="EmployeePrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> ExistsAsync(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_ExistsByPrimaryKey", cancellationToken);
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
				var executionResult = dh.ExecuteScalar("gsp_Employees_ExistsByField");
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
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_Employees_ExistsByField", cancellationToken);
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
		/// <param name="employeeID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employee</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employee? SelectOne(int employeeID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return SelectOne(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously return an object representing the record matching the primary key information specified.
		/// </summary>
		///
		/// <param name="employeeID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employee</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employee?> SelectOneAsync(int employeeID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return await SelectOneAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will Delete one row from the database using the primary key information
		/// </summary>
		///
		/// <param name="employeeID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool Delete(int employeeID, DatabaseHelper? databaseHelper = null) 
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return Delete(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously Delete one row from the database using the primary key information
		/// </summary>
		///
		/// <param name="employeeID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> DeleteAsync(int employeeID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default) 
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return await DeleteAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will check if a row exists in the table using the value of the primary key
		/// </summary>
		///
		/// <param name="employeeID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool Exists(int employeeID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return Exists(pk, databaseHelper);
		}

		/// <summary>
		/// This method will asynchronously check if a row exists in the table using the value of the primary key
		/// </summary>
		///
		/// <param name="employeeID" type="int">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>True if succeeded</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> ExistsAsync(int employeeID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return await ExistsAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employee? SelectOneWithEmployeesUsingReportsTo(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employee? obj = null;
			
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
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectOneWithEmployeesUsingReportsTo");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (dr.Read())
					{
						obj = new Employee(databaseHelper);
						PopulateObjectFromReader(obj,dr);
				
						dr.NextResult();
				
						//Get the child records.
						obj.EmployeesUsingReportsTo = Employee.PopulateObjectsFromReader(dr, databaseHelper);
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
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employee?> SelectOneWithEmployeesUsingReportsToAsync(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employee? obj = null;
			
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectOneWithEmployeesUsingReportsTo", cancellationToken);			
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (await dr.ReadAsync(cancellationToken))
				{
					obj = new Employee(databaseHelper);
					PopulateObjectFromReader(obj,dr);
				
					dr.NextResult();
				
					//Get the child records.
					obj.EmployeesUsingReportsTo = await Employee.PopulateObjectsFromReaderAsync(dr, databaseHelper, cancellationToken);
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
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employee? SelectOneWithEmployeeTerritoriesUsingEmployeeID(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employee? obj = null;
			
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
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectOneWithEmployeeTerritoriesUsingEmployeeID");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (dr.Read())
					{
						obj = new Employee(databaseHelper);
						PopulateObjectFromReader(obj,dr);
				
						dr.NextResult();
				
						//Get the child records.
						obj.EmployeeTerritoriesUsingEmployeeID = EmployeeTerritory.PopulateObjectsFromReader(dr, databaseHelper);
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
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employee?> SelectOneWithEmployeeTerritoriesUsingEmployeeIDAsync(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employee? obj = null;
			
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectOneWithEmployeeTerritoriesUsingEmployeeID", cancellationToken);			
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (await dr.ReadAsync(cancellationToken))
				{
					obj = new Employee(databaseHelper);
					PopulateObjectFromReader(obj,dr);
				
					dr.NextResult();
				
					//Get the child records.
					obj.EmployeeTerritoriesUsingEmployeeID = await EmployeeTerritory.PopulateObjectsFromReaderAsync(dr, databaseHelper, cancellationToken);
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
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employee? SelectOneWithOrdersUsingEmployeeID(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
      dh.ShouldUseBackupServer = false;
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employee? obj = null;
			
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
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectOneWithOrdersUsingEmployeeID");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (dr.Read())
					{
						obj = new Employee(databaseHelper);
						PopulateObjectFromReader(obj,dr);
				
						dr.NextResult();
				
						//Get the child records.
						obj.OrdersUsingEmployeeID = Order.PopulateObjectsFromReader(dr, databaseHelper);
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
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employee?> SelectOneWithOrdersUsingEmployeeIDAsync(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.ShouldUseBackupServer = false;
      DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employee? obj = null;
			
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectOneWithOrdersUsingEmployeeID", cancellationToken);			
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				if (await dr.ReadAsync(cancellationToken))
				{
					obj = new Employee(databaseHelper);
					PopulateObjectFromReader(obj,dr);
				
					dr.NextResult();
				
					//Get the child records.
					obj.OrdersUsingEmployeeID = await Order.PopulateObjectsFromReaderAsync(dr, databaseHelper, cancellationToken);
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
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employee? SelectOneWithEmployeesUsingReportsTo(int employeeID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return SelectOneWithEmployeesUsingReportsTo(pk, databaseHelper);
		}
		/// <summary>
		/// This method will asynchronously get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employee?> SelectOneWithEmployeesUsingReportsToAsync(int employeeID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return await SelectOneWithEmployeesUsingReportsToAsync(pk, databaseHelper, cancellationToken);
		}
		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employee? SelectOneWithEmployeeTerritoriesUsingEmployeeID(int employeeID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return SelectOneWithEmployeeTerritoriesUsingEmployeeID(pk, databaseHelper);
		}
		/// <summary>
		/// This method will asynchronously get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employee?> SelectOneWithEmployeeTerritoriesUsingEmployeeIDAsync(int employeeID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return await SelectOneWithEmployeeTerritoriesUsingEmployeeIDAsync(pk, databaseHelper, cancellationToken);
		}
		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employee? SelectOneWithOrdersUsingEmployeeID(int employeeID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return SelectOneWithOrdersUsingEmployeeID(pk, databaseHelper);
		}
		/// <summary>
		/// This method will asynchronously get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employee?> SelectOneWithOrdersUsingEmployeeIDAsync(int employeeID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return await SelectOneWithOrdersUsingEmployeeIDAsync(pk, databaseHelper, cancellationToken);
		}
		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employees SelectAllByForeignKeyReportsTo(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employees? obj = null;
			
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
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectAllByForeignKeyReportsTo");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				obj = new Employees();
				obj = Employee.PopulateObjectsFromReaderWithCheckingReader(dr, databaseHelper);
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
		/// This method will get row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<Employees></returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employees> SelectAllByForeignKeyReportsToAsync(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employees? obj = null;
			
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectAllByForeignKeyReportsTo", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				obj = new Employees();
				obj = await Employee.PopulateObjectsFromReaderWithCheckingReaderAsync(dr, databaseHelper, cancellationToken);
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
		/// This method will count row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static int SelectAllCountByForeignKeyReportsTo(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employees? obj = null;
			int count = 0;
			
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
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectAllCountByForeignKeyReportsTo");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				
				using (DataTable dt = new DataTable())
				{
					dt.Load(dr);
					count = Convert.ToInt32(dt.Rows[0][0]);
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
			
			return count;
			
		}

		/// <summary>
		/// This method will count row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<Employees></returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<int> SelectAllCountByForeignKeyReportsToAsync(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employees? obj = null;
			int count = 0;
			
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectAllCountByForeignKeyReportsTo", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				
				using (DataTable dt = new DataTable())
				{
					dt.Load(dr);
					count = Convert.ToInt32(dt.Rows[0][0]);
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
			
			return count;
			
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employees SelectAllByForeignKeyReportsToPaged(EmployeePrimaryKey pk, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employees? obj = null;
			
			try
			{
			
				// Pass the values of all key parameters to the stored procedure.
				System.Collections.Specialized.NameValueCollection nvc = pk.GetKeysAndValues();
				foreach (string nvcKey in nvc.Keys)
				{
					dh.AddParameter("@" + nvcKey,nvc[nvcKey] );
				}
				dh.AddParameter("@PageSize",pageSize);
				dh.AddParameter("@PageNumber", pageNumber);
				dh.AddParameter("@OrderByStatement", orderByStatement );
			
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = dh.ExecuteReader("gsp_Employees_SelectAllByForeignKeyReportsToPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				obj = new Employees();
				obj = Employee.PopulateObjectsFromReaderWithCheckingReader(dr, databaseHelper);
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
		/// This method will get row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employees> SelectAllByForeignKeyReportsToPagedAsync(EmployeePrimaryKey pk, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			Employees? obj = null;
			
			try
			{
			
				// Pass the values of all key parameters to the stored procedure.
				System.Collections.Specialized.NameValueCollection nvc = pk.GetKeysAndValues();
				foreach (string nvcKey in nvc.Keys)
				{
					dh.AddParameter("@" + nvcKey,nvc[nvcKey] );
				}
				dh.AddParameter("@PageSize",pageSize);
				dh.AddParameter("@PageNumber", pageNumber);
				dh.AddParameter("@OrderByStatement", orderByStatement );
			
				// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_Employees_SelectAllByForeignKeyReportsToPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				obj = new Employees();
				obj = await Employee.PopulateObjectsFromReaderWithCheckingReaderAsync(dr, databaseHelper, cancellationToken);
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
		/// This method will delete row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be deleted.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of boolean type as an indicator for operation success .</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool DeleteAllByForeignKeyReportsTo(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			
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
			
				
			//Try Primary Server
			var executionResult = dh.ExecuteNonQuery("gsp_Employees_DeleteAllByForeignKeyReportsTo");
			wasExecutionSuccessful = executionResult.WasSuccessful;

			//Try Backup Server if Primary Server Succeeds (to keep both servers in sync)
			if (dh.ShouldUseBackupServer && wasExecutionSuccessful)
			{
				try
				{
					bool backupExecutionState = false;

					dh.ExecuteNonQuery("gsp_Employees_DeleteAllByForeignKeyReportsTo", CommandType.StoredProcedure, ConnectionState.CloseOnExit);
				}
				catch (Exception ex)
				{
					dh.ProcessException(ex, false);
				}
			}
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally 
			{
				dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will delete row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="EmployeesPrimaryKey">Primary Key information based on which data is to be deleted.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of boolean type as an indicator for operation success .</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> DeleteAllByForeignKeyReportsToAsync(EmployeePrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
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
            var executionResult = await dh.ExecuteNonQueryAsync("gsp_Employees_DeleteAllByForeignKeyReportsTo", cancellationToken);
            wasExecutionSuccessful = executionResult.WasSuccessful;

            //Try Backup Server if Primary Server Succeeds (to keep both servers in sync)
            if (dh.ShouldUseBackupServer && wasExecutionSuccessful && dh.BackupConnectionString.Length != 0)
            {
                try
                {
                    bool backupExecutionState = false;

                    await dh.ExecuteNonQueryAsync("gsp_Employees_DeleteAllByForeignKeyReportsTo", CommandType.StoredProcedure, ConnectionState.CloseOnExit, cancellationToken);
                }
                catch (Exception ex)
                {
                    dh.ProcessException(ex, false);
                }
            }
			
			}
			catch (Exception ex)
			{
				dh.ProcessException(ex);
			}
			finally
			{
				dh.Dispose();
			}
			
			return wasExecutionSuccessful;
			
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employees SelectAllByForeignKeyReportsTo(int employeeID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return SelectAllByForeignKeyReportsTo(pk, databaseHelper);
		}

		/// <summary>
		/// This method will get row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<Employees></returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employees> SelectAllByForeignKeyReportsToAsync(int employeeID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return await SelectAllByForeignKeyReportsToAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will count row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static int SelectAllCountByForeignKeyReportsTo(int employeeID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return SelectAllCountByForeignKeyReportsTo(pk, databaseHelper);
		}

		/// <summary>
		/// This method will count row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<Employees></returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<int> SelectAllCountByForeignKeyReportsToAsync(int employeeID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return await SelectAllCountByForeignKeyReportsToAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employees SelectAllByForeignKeyReportsToPaged(int employeeID, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return SelectAllByForeignKeyReportsToPaged(pk, pageNumber, pageSize, orderByStatement, databaseHelper);
		}

		/// <summary>
		/// This method will get row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employees> SelectAllByForeignKeyReportsToPagedAsync(int employeeID, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return await SelectAllByForeignKeyReportsToPagedAsync(pk, pageNumber, pageSize, orderByStatement, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will delete row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be deleted.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of boolean type as an indicator for operation success .</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static bool DeleteAllByForeignKeyReportsTo(int employeeID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return DeleteAllByForeignKeyReportsTo(pk, databaseHelper);
		}

		/// <summary>
		/// This method will delete row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="employeeID" type="EmployeesPrimaryKey">Primary Key information based on which data is to be deleted.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of boolean type as an indicator for operation success .</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<bool> DeleteAllByForeignKeyReportsToAsync(int employeeID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new EmployeePrimaryKey(employeeID);
			return await DeleteAllByForeignKeyReportsToAsync(pk, databaseHelper, cancellationToken);
		}

		#endregion	
		
		#region Methods (Private)
		
		/// <summary>
		/// Populates the fields of a single objects from the columns found in an open reader.
		/// </summary>
		/// <param name="obj" type="Employees">Object of Employees to populate</param>
		/// <param name="rdr" type="IDataReader">An object that implements the IDataReader interface</param>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static void PopulateObjectFromReader(EmployeeBase obj,IDataReader rdr) 
		{

			int ord_EmployeeID = rdr.GetOrdinal(EmployeeFields.EmployeeID);
			int ord_LastName = rdr.GetOrdinal(EmployeeFields.LastName);
			int ord_FirstName = rdr.GetOrdinal(EmployeeFields.FirstName);
			int ord_Title = rdr.GetOrdinal(EmployeeFields.Title);
			int ord_TitleOfCourtesy = rdr.GetOrdinal(EmployeeFields.TitleOfCourtesy);
			int ord_BirthDate = rdr.GetOrdinal(EmployeeFields.BirthDate);
			int ord_HireDate = rdr.GetOrdinal(EmployeeFields.HireDate);
			int ord_Address = rdr.GetOrdinal(EmployeeFields.Address);
			int ord_City = rdr.GetOrdinal(EmployeeFields.City);
			int ord_Region = rdr.GetOrdinal(EmployeeFields.Region);
			int ord_PostalCode = rdr.GetOrdinal(EmployeeFields.PostalCode);
			int ord_Country = rdr.GetOrdinal(EmployeeFields.Country);
			int ord_HomePhone = rdr.GetOrdinal(EmployeeFields.HomePhone);
			int ord_Extension = rdr.GetOrdinal(EmployeeFields.Extension);
			int ord_Photo = rdr.GetOrdinal(EmployeeFields.Photo);
			int ord_Notes = rdr.GetOrdinal(EmployeeFields.Notes);
			int ord_ReportsTo = rdr.GetOrdinal(EmployeeFields.ReportsTo);
			int ord_PhotoPath = rdr.GetOrdinal(EmployeeFields.PhotoPath);

			obj.EmployeeID = rdr.GetInt32(ord_EmployeeID);
			obj.LastName = rdr.GetString(ord_LastName);
			obj.FirstName = rdr.GetString(ord_FirstName);
			if (!rdr.IsDBNull(ord_Title))
			{
				obj.Title = rdr.GetString(ord_Title);
			}
			
			if (!rdr.IsDBNull(ord_TitleOfCourtesy))
			{
				obj.TitleOfCourtesy = rdr.GetString(ord_TitleOfCourtesy);
			}
			
			if (!rdr.IsDBNull(ord_BirthDate))
			{
				obj.BirthDate = rdr.GetDateTime(ord_BirthDate);
			}
			
			if (!rdr.IsDBNull(ord_HireDate))
			{
				obj.HireDate = rdr.GetDateTime(ord_HireDate);
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
			
			if (!rdr.IsDBNull(ord_HomePhone))
			{
				obj.HomePhone = rdr.GetString(ord_HomePhone);
			}
			
			if (!rdr.IsDBNull(ord_Extension))
			{
				obj.Extension = rdr.GetString(ord_Extension);
			}
			
			if (!rdr.IsDBNull(ord_Photo))
			{
				obj.Photo = (System.Byte[])rdr.GetValue(ord_Photo);
			}
			
			if (!rdr.IsDBNull(ord_Notes))
			{
				obj.Notes = rdr.GetString(ord_Notes);
			}
			
			if (!rdr.IsDBNull(ord_ReportsTo))
			{
				obj.ReportsTo = rdr.GetInt32(ord_ReportsTo);
			}
			
			if (!rdr.IsDBNull(ord_PhotoPath))
			{
				obj.PhotoPath = rdr.GetString(ord_PhotoPath);
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
		/// <returns>Object of Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static Employees PopulateObjectsFromReader(IDataReader rdr, DatabaseHelper? databaseHelper)
		{
			Employees list = new Employees();
			
			while (rdr.Read())
			{
				Employee obj = new Employee(databaseHelper);
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
		/// <returns>Object of Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public static async Task<Employees> PopulateObjectsFromReaderAsync(DbDataReader rdr, DatabaseHelper? databaseHelper, CancellationToken cancellationToken)
		{
			Employees list = new Employees();
			
			while (await rdr.ReadAsync(cancellationToken))
			{
				Employee obj = new Employee(databaseHelper);
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
		/// <returns>Object of Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		internal static Employees PopulateObjectsFromReaderWithCheckingReader(IDataReader rdr, DatabaseHelper databaseHelper) 
		{

			Employees list = new Employees();
			
            if (rdr.Read())
			{
				Employee obj = new Employee(databaseHelper);
				PopulateObjectFromReader(obj, rdr);
				list.Add(obj);
				while (rdr.Read())
				{
					obj = new Employee(databaseHelper);
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
		/// <returns>Object of Employees</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		internal static async Task<Employees> PopulateObjectsFromReaderWithCheckingReaderAsync(DbDataReader rdr, DatabaseHelper databaseHelper, CancellationToken cancellationToken) 
		{

			Employees list = new Employees();
			
            if (await rdr.ReadAsync(cancellationToken))
			{
				Employee obj = new Employee(databaseHelper);
				PopulateObjectFromReader(obj, rdr);
				list.Add(obj);
				while (await rdr.ReadAsync(cancellationToken))
				{
					obj = new Employee(databaseHelper);
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
		/// Populates the parameters for the Employees table stored procedures.
		/// </summary>
		///
		/// <param name="dh" type="DatabaseHelper">DatabaseHelper to populate parameters on</param>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			06/04/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		internal void PopulateDatabaseHelperParameters(DatabaseHelper dh)
		{
			// Pass the value of '_employeeID' as parameter 'EmployeeID' of the stored procedure.
			dh.AddParameter("@EmployeeID", _employeeID);

			// Pass the value of '_lastName' as parameter 'LastName' of the stored procedure.
			dh.AddParameter("@LastName", _lastName);

			// Pass the value of '_firstName' as parameter 'FirstName' of the stored procedure.
			dh.AddParameter("@FirstName", _firstName);

			// Pass the value of '_title' as parameter 'Title' of the stored procedure.
			if(_title is not null)
			  dh.AddParameter("@Title", _title);
			else
			  dh.AddParameter("@Title", DBNull.Value );

			// Pass the value of '_titleOfCourtesy' as parameter 'TitleOfCourtesy' of the stored procedure.
			if(_titleOfCourtesy is not null)
			  dh.AddParameter("@TitleOfCourtesy", _titleOfCourtesy);
			else
			  dh.AddParameter("@TitleOfCourtesy", DBNull.Value );

			// Pass the value of '_birthDate' as parameter 'BirthDate' of the stored procedure.
			if(_birthDate is not null)
			  dh.AddParameter("@BirthDate", _birthDate);
			else
			  dh.AddParameter("@BirthDate", DBNull.Value );

			// Pass the value of '_hireDate' as parameter 'HireDate' of the stored procedure.
			if(_hireDate is not null)
			  dh.AddParameter("@HireDate", _hireDate);
			else
			  dh.AddParameter("@HireDate", DBNull.Value );

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

			// Pass the value of '_homePhone' as parameter 'HomePhone' of the stored procedure.
			if(_homePhone is not null)
			  dh.AddParameter("@HomePhone", _homePhone);
			else
			  dh.AddParameter("@HomePhone", DBNull.Value );

			// Pass the value of '_extension' as parameter 'Extension' of the stored procedure.
			if(_extension is not null)
			  dh.AddParameter("@Extension", _extension);
			else
			  dh.AddParameter("@Extension", DBNull.Value );

			// Pass the value of '_photo' as parameter 'Photo' of the stored procedure.
			if(_photo is not null)
			  dh.AddParameter("@Photo", _photo , DbType.Binary );
			else
			  dh.AddParameter("@Photo", DBNull.Value , DbType.Binary );
			  
			// Pass the value of '_notes' as parameter 'Notes' of the stored procedure.
			if(_notes is not null)
			  dh.AddParameter("@Notes", _notes);
			else
			  dh.AddParameter("@Notes", DBNull.Value );

			// Pass the value of '_reportsTo' as parameter 'ReportsTo' of the stored procedure.
			if(_reportsTo is not null)
			  dh.AddParameter("@ReportsTo", _reportsTo);
			else
			  dh.AddParameter("@ReportsTo", DBNull.Value );

			// Pass the value of '_photoPath' as parameter 'PhotoPath' of the stored procedure.
			if(_photoPath is not null)
			  dh.AddParameter("@PhotoPath", _photoPath);
			else
			  dh.AddParameter("@PhotoPath", DBNull.Value );

			// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
			dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

		}

	
	#endregion

	}
}
