//
// Class	:	OrderDetailBase.cs
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
	public partial class OrderDetailFields
	{
		public const string OrderID                   = "OrderID";
		public const string ProductID                 = "ProductID";
		public const string UnitPrice                 = "UnitPrice";
		public const string Quantity                  = "Quantity";
		public const string Discount                  = "Discount";
	}
	
	/// <summary>
	/// Data access class for the "Order Details" table.
	/// </summary>
	[Serializable]
	public class OrderDetailBase : TrackableEntity<OrderDetailBase>
	{
		
		#region Class Level Variables
		
		private DatabaseHelper? _databaseHelper = null;
    
		private int            	_orderID                 	= 0;
		private int            ?	_originalOrderID         	= 0;
		private int            	_productID               	= 0;
		private int            ?	_originalProductID       	= 0;
		private decimal?       	_unitPrice               	= 0M;
		private short?         	_quantity                	= 1;
		private float?         	_discount                	= 0;
		
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
		public OrderDetailBase(DatabaseHelper? databaseHelper = null) { 
                _databaseHelper = databaseHelper;
                TakeSnapshot();
          }
					
		#endregion
		
		#region Properties

		
		/// <summary>
		/// Returns the identifier of the persistent object. Mandatory.
		/// </summary>
		[Trackable]
		public int OrderID
		{
			get 
			{ 
				return _orderID;
			}
			set 
			{
			
				if (_originalOrderID is null || !_originalOrderID.HasValue)
						_originalOrderID = _orderID;
				_orderID = value; 
			}
		}
      

		
		/// <summary>
		/// Returns the identifier of the persistent object. Mandatory.
		/// </summary>
		[Trackable]
		public int ProductID
		{
			get 
			{ 
				return _productID;
			}
			set 
			{
			
				if (_originalProductID is null || !_originalProductID.HasValue)
						_originalProductID = _productID;
				_productID = value; 
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "UnitPrice" field.  If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public decimal? UnitPrice
		{
			get 
			{ 
				return _unitPrice;
			}
			set 
			{
			
				_unitPrice = value; 
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Quantity" field.  If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public short? Quantity
		{
			get 
			{ 
				return _quantity;
			}
			set 
			{
			
				_quantity = value; 
			}
		}
      

		
		/// <summary>
		/// This property is mapped to the "Discount" field.  If null, the database will use the default value.
		/// </summary>
		[Trackable]
		public float? Discount
		{
			get 
			{ 
				return _discount;
			}
			set 
			{
			
				_discount = value; 
			}
		}
      		
		//This property is related to the table name that exist in database
		
		public static string TableName
		{
			get 
			{ 
				  return "Order Details";
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
					var executionResult = dh.ExecuteScalar("gsp_OrderDetails_Insert");
					wasExecutionSuccessful = executionResult.WasSuccessful;
					if (wasExecutionSuccessful)
                    {
                        TakeSnapshot();
                    }
				}
				else //Try Primary Server
				{
					var executionResult = dh.ExecuteReader("gsp_OrderDetails_Insert");
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
				     var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_Insert", cancellationToken);
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server 
			    {
				      var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_Insert", cancellationToken);
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
			
			
				// Pass the value of '_orderID' as parameter 'OrderID' of the stored procedure.
				dh.AddParameter("@OrderID", _originalOrderID.OrIfNullOrEmpty(_orderID));
							// Pass the value of '_productID' as parameter 'ProductID' of the stored procedure.
				dh.AddParameter("@ProductID", _originalProductID.OrIfNullOrEmpty(_productID));
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
                var executionResult = dh.ExecuteScalar("gsp_OrderDetails_Update");
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
			
			
				// Pass the value of '_orderID' as parameter 'OrderID' of the stored procedure.
				dh.AddParameter("@OrderID", _originalOrderID.OrIfNullOrEmpty(_orderID));
							// Pass the value of '_productID' as parameter 'ProductID' of the stored procedure.
				dh.AddParameter("@ProductID", _originalProductID.OrIfNullOrEmpty(_productID));
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_Update");
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
			
			
				// Pass the value of '_orderID' as parameter 'OrderID' of the stored procedure.
				dh.AddParameter("@OrderID", _originalOrderID.OrIfNullOrEmpty(_orderID));
							// Pass the value of '_productID' as parameter 'ProductID' of the stored procedure.
				dh.AddParameter("@ProductID", _originalProductID.OrIfNullOrEmpty(_productID));
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_Update", cancellationToken);
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
				     var executionResult = dh.ExecuteScalar("gsp_OrderDetails_Upsert");
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server
			    {
				      var executionResult = dh.ExecuteReader("gsp_OrderDetails_Upsert");
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
				     var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_Upsert", cancellationToken);
					 wasExecutionSuccessful = executionResult.WasSuccessful;
					 if (wasExecutionSuccessful)
                     {
                         TakeSnapshot();
                     }
			    }
			    else //Try Primary Server 
			    {
				      var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_Upsert", cancellationToken);
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
			
			
				// Pass the value of '_orderID' as parameter 'OrderID' of the stored procedure.
				dh.AddParameter("@OrderID", _orderID );
							// Pass the value of '_productID' as parameter 'ProductID' of the stored procedure.
				dh.AddParameter("@ProductID", _productID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			
			
                //Try Primary Server
                var executionResult = dh.ExecuteScalar("gsp_OrderDetails_Delete");
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
			
			
				// Pass the value of '_orderID' as parameter 'OrderID' of the stored procedure.
				dh.AddParameter("@OrderID", _orderID );
							// Pass the value of '_productID' as parameter 'ProductID' of the stored procedure.
				dh.AddParameter("@ProductID", _productID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_Delete");
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
			
			
				// Pass the value of '_orderID' as parameter 'OrderID' of the stored procedure.
				dh.AddParameter("@OrderID", _orderID );
							// Pass the value of '_productID' as parameter 'ProductID' of the stored procedure.
				dh.AddParameter("@ProductID", _productID );
							// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
				dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

                //Try Primary Server
                var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_Delete", cancellationToken);
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
		/// <param name="pk" type="OrderDetailPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static bool Delete(OrderDetailPrimaryKey pk, DatabaseHelper? databaseHelper = null) 
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
                var executionResult = dh.ExecuteScalar("gsp_OrderDetails_Delete");
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
		/// <param name="pk" type="OrderDetailPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static async Task<bool> DeleteAsync(OrderDetailPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default) 
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_Delete", cancellationToken);
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
		/// <param name="field" type="OrderDetailFields">Field of the class OrderDetail</param>
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
                var executionResult = dh.ExecuteScalar("gsp_OrderDetails_DeleteByField");
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
		/// <param name="field" type="OrderDetailFields">Field of the class OrderDetail</param>
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_DeleteByField", cancellationToken);
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
                var executionResult = dh.ExecuteScalar("gsp_OrderDetails_DeleteByField");
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
                var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_DeleteByField", cancellationToken);
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
		/// <param name="pk" type="OrderDetailPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetail</returns>
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
		public static OrderDetail? SelectOne(OrderDetailPrimaryKey pk, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectByPrimaryKey");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				if (dr.Read())
				{
					OrderDetail obj = new OrderDetail(databaseHelper);	
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
		/// <param name="pk" type="OrderDetailPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetail</returns>
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
		public static async Task<OrderDetail?> SelectOneAsync(OrderDetailPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectByPrimaryKey", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				if (dr.Read())
				{
					OrderDetail obj = new OrderDetail(databaseHelper);	
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
		/// <returns>list of objects of class OrderDetail in the form of object of OrderDetails </returns>
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
		public static OrderDetails SelectAll(DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectAll");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				OrderDetails list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class OrderDetail in the form of object of OrderDetails </returns>
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
		public static async Task<OrderDetails> SelectAllAsync(DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectAll", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				OrderDetails list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <returns>list of objects of class OrderDetail in the form of object of OrderDetails </returns>
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
		public static OrderDetails SelectAll(Func<IBaseQueryBuilder, IQuery> queryBuilderFunc, int? numberOfRecordsToReturn = null, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectAll");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				OrderDetails list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class OrderDetail in the form of object of OrderDetails </returns>
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
		public static async Task<OrderDetails> SelectAllAsync(Func<IBaseQueryBuilder, IQuery> queryBuilderFunc, int? numberOfRecordsToReturn = null, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectAll", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				OrderDetails list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <param name="field" type="string">Field of the class OrderDetail</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class OrderDetail in the form of an object of class OrderDetails</returns>
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
		public static OrderDetails SelectByField(string field, object fieldValue, DatabaseHelper? databaseHelper = null)
		{
			return SelectByField(field, fieldValue, null, TypeOperation.Equal, null, null, databaseHelper);
			
		}

		/// <summary>
		/// Deprecated. Use SelectByFieldAsync(string field, object fieldValue, object fieldValue2, TypeOperation typeOperation) instead. This method will asynchronously get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class OrderDetail</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class OrderDetail in the form of an object of class OrderDetails</returns>
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
		public static async Task<OrderDetails> SelectByFieldAsync(string field, object fieldValue, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			return await SelectByFieldAsync(field, fieldValue, null, TypeOperation.Equal, null, null, databaseHelper, cancellationToken);
			
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified
		/// </summary>
		///
		/// <param name="field" type="string">Field of the class OrderDetail</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class OrderDetail in the form of an object of class OrderDetails</returns>
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
		public static OrderDetails SelectByField(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, string? orderByField = null, string orderByDirection = "ASC", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectByField");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;	
				OrderDetails list = PopulateObjectsFromReader(dr, dh);
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
		/// <param name="field" type="string">Field of the class OrderDetail</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class OrderDetail in the form of an object of class OrderDetails</returns>
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
		public static async Task<OrderDetails> SelectByFieldAsync(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, string? orderByField = null, string orderByDirection = "ASC", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectByField", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				OrderDetails list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <returns>list of objects of class OrderDetail in the form of object of OrderDetails </returns>
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
		public static OrderDetails SelectAllPaged(int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectAllPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				OrderDetails list = PopulateObjectsFromReader(dr, dh);
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
		/// <returns>list of objects of class OrderDetail in the form of object of OrderDetails </returns>
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
		public static async Task<OrderDetails> SelectAllPagedAsync(int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectAllPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				OrderDetails list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
		/// <param name="field" type="string">Field of the class OrderDetail</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="orderByStatement" type="string">The field value to number.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class OrderDetail in the form of an object of class OrderDetails</returns>
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
		public static OrderDetails SelectByFieldPaged(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectByFieldPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				OrderDetails list = PopulateObjectsFromReader(dr, dh);
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
		/// <param name="field" type="string">Field of the class OrderDetail</param>
		/// <param name="fieldValue" type="object">Value for the field specified.</param>
		/// <param name="fieldValue2" type="object">Value for the field specified.</param>
		/// <param name="typeOperation" type="TypeOperation">Operator that is used if fieldValue2=null or fieldValue2="".</param>
		/// <param name="orderByStatement" type="string">The field value to number.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>List of object of class OrderDetail in the form of an object of class OrderDetails</returns>
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
		public static async Task<OrderDetails> SelectByFieldPagedAsync(string field, object fieldValue, object? fieldValue2, TypeOperation typeOperation, int pageNumber, int pageSize, string orderByStatement = "", DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectByFieldPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				OrderDetails list = await PopulateObjectsFromReaderAsync(dr, dh, cancellationToken);
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
			
				var executionResult = dh.ExecuteScalar("gsp_OrderDetails_SelectAllCount");
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_SelectAllCount", cancellationToken);
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
			
				var executionResult = dh.ExecuteScalar("gsp_OrderDetails_SelectAllCount");
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_SelectAllCount", cancellationToken);
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
		/// <param name="field" type="OrderDetailFields">Field of the class OrderDetail</param>
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
			
				var executionResult = dh.ExecuteScalar("gsp_OrderDetails_ExistsByField");
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
		/// <param name="field" type="OrderDetailFields">Field of the class OrderDetail</param>
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
			

				var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_ExistsByField", cancellationToken);
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
		/// <param name="pk" type="OrderDetailPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static bool Exists(OrderDetailPrimaryKey pk, DatabaseHelper? databaseHelper = null)
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
			
				var executionResult = dh.ExecuteScalar("gsp_OrderDetails_ExistsByPrimaryKey");
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
		/// <param name="pk" type="OrderDetailPrimaryKey">Primary Key information based on which data is to be fetched.</param>
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
		public static async Task<bool> ExistsAsync(OrderDetailPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_ExistsByPrimaryKey", cancellationToken);
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
			
				var executionResult = dh.ExecuteScalar("gsp_OrderDetails_ExistsByField");
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
			
				var executionResult = await dh.ExecuteScalarAsync("gsp_OrderDetails_ExistsByField", cancellationToken);
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
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="pk" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static OrderDetails SelectAllByForeignKeyProductID(ProductPrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
			
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectAllByForeignKeyProductID");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				obj = new OrderDetails();
				obj = OrderDetail.PopulateObjectsFromReaderWithCheckingReader(dr, databaseHelper);
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
		/// <param name="pk" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<OrderDetails></returns>
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
		public static async Task<OrderDetails> SelectAllByForeignKeyProductIDAsync(ProductPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
			
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectAllByForeignKeyProductID", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				obj = new OrderDetails();
				obj = await OrderDetail.PopulateObjectsFromReaderWithCheckingReaderAsync(dr, databaseHelper, cancellationToken);
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
		/// <param name="pk" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static int SelectAllCountByForeignKeyProductID(ProductPrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectAllCountByForeignKeyProductID");
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
		/// <param name="pk" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<OrderDetails></returns>
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
		public static async Task<int> SelectAllCountByForeignKeyProductIDAsync(ProductPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectAllCountByForeignKeyProductID", cancellationToken);
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
		/// <param name="pk" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static OrderDetails SelectAllByForeignKeyProductIDPaged(ProductPrimaryKey pk, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
			
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectAllByForeignKeyProductIDPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				obj = new OrderDetails();
				obj = OrderDetail.PopulateObjectsFromReaderWithCheckingReader(dr, databaseHelper);
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
		/// <param name="pk" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static async Task<OrderDetails> SelectAllByForeignKeyProductIDPagedAsync(ProductPrimaryKey pk, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
			
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectAllByForeignKeyProductIDPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				obj = new OrderDetails();
				obj = await OrderDetail.PopulateObjectsFromReaderWithCheckingReaderAsync(dr, databaseHelper, cancellationToken);
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
		/// <param name="pk" type="ProductsPrimaryKey">Primary Key information based on which data is to be deleted.</param>
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
		public static bool DeleteAllByForeignKeyProductID(ProductPrimaryKey pk, DatabaseHelper? databaseHelper = null)
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
			var executionResult = dh.ExecuteNonQuery("gsp_OrderDetails_DeleteAllByForeignKeyProductID");
			wasExecutionSuccessful = executionResult.WasSuccessful;

			//Try Backup Server if Primary Server Succeeds (to keep both servers in sync)
			if (dh.ShouldUseBackupServer && wasExecutionSuccessful)
			{
				try
				{
					bool backupExecutionState = false;

					dh.ExecuteNonQuery("gsp_OrderDetails_DeleteAllByForeignKeyProductID", CommandType.StoredProcedure, ConnectionState.CloseOnExit);
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
		/// <param name="pk" type="ProductsPrimaryKey">Primary Key information based on which data is to be deleted.</param>
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
		public static async Task<bool> DeleteAllByForeignKeyProductIDAsync(ProductPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
            var executionResult = await dh.ExecuteNonQueryAsync("gsp_OrderDetails_DeleteAllByForeignKeyProductID", cancellationToken);
            wasExecutionSuccessful = executionResult.WasSuccessful;

            //Try Backup Server if Primary Server Succeeds (to keep both servers in sync)
            if (dh.ShouldUseBackupServer && wasExecutionSuccessful && dh.BackupConnectionString.Length != 0)
            {
                try
                {
                    bool backupExecutionState = false;

                    await dh.ExecuteNonQueryAsync("gsp_OrderDetails_DeleteAllByForeignKeyProductID", CommandType.StoredProcedure, ConnectionState.CloseOnExit, cancellationToken);
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
		/// <param name="pk" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static OrderDetails SelectAllByForeignKeyOrderID(OrderPrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
			
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectAllByForeignKeyOrderID");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				obj = new OrderDetails();
				obj = OrderDetail.PopulateObjectsFromReaderWithCheckingReader(dr, databaseHelper);
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
		/// <param name="pk" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<OrderDetails></returns>
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
		public static async Task<OrderDetails> SelectAllByForeignKeyOrderIDAsync(OrderPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
			
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectAllByForeignKeyOrderID", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				obj = new OrderDetails();
				obj = await OrderDetail.PopulateObjectsFromReaderWithCheckingReaderAsync(dr, databaseHelper, cancellationToken);
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
		/// <param name="pk" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static int SelectAllCountByForeignKeyOrderID(OrderPrimaryKey pk, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectAllCountByForeignKeyOrderID");
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
		/// <param name="pk" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<OrderDetails></returns>
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
		public static async Task<int> SelectAllCountByForeignKeyOrderIDAsync(OrderPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectAllCountByForeignKeyOrderID", cancellationToken);
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
		/// <param name="pk" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static OrderDetails SelectAllByForeignKeyOrderIDPaged(OrderPrimaryKey pk, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
            dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
			
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
			
				var executionResult = dh.ExecuteReader("gsp_OrderDetails_SelectAllByForeignKeyOrderIDPaged");
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
				obj = new OrderDetails();
				obj = OrderDetail.PopulateObjectsFromReaderWithCheckingReader(dr, databaseHelper);
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
		/// <param name="pk" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static async Task<OrderDetails> SelectAllByForeignKeyOrderIDPagedAsync(OrderPrimaryKey pk, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			DatabaseHelper dh = new DatabaseHelper(databaseHelper);
			DbDataReader? dr = null;
			dh.CommandTimeOut = CommandTimeOut;
			bool wasExecutionSuccessful = false;
			OrderDetails? obj = null;
			
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
			
				var executionResult = await dh.ExecuteReaderAsync("gsp_OrderDetails_SelectAllByForeignKeyOrderIDPaged", cancellationToken);
				dr = executionResult.Result!;
				wasExecutionSuccessful = executionResult.WasSuccessful;
			
				obj = new OrderDetails();
				obj = await OrderDetail.PopulateObjectsFromReaderWithCheckingReaderAsync(dr, databaseHelper, cancellationToken);
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
		/// <param name="pk" type="OrdersPrimaryKey">Primary Key information based on which data is to be deleted.</param>
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
		public static bool DeleteAllByForeignKeyOrderID(OrderPrimaryKey pk, DatabaseHelper? databaseHelper = null)
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
			var executionResult = dh.ExecuteNonQuery("gsp_OrderDetails_DeleteAllByForeignKeyOrderID");
			wasExecutionSuccessful = executionResult.WasSuccessful;

			//Try Backup Server if Primary Server Succeeds (to keep both servers in sync)
			if (dh.ShouldUseBackupServer && wasExecutionSuccessful)
			{
				try
				{
					bool backupExecutionState = false;

					dh.ExecuteNonQuery("gsp_OrderDetails_DeleteAllByForeignKeyOrderID", CommandType.StoredProcedure, ConnectionState.CloseOnExit);
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
		/// <param name="pk" type="OrdersPrimaryKey">Primary Key information based on which data is to be deleted.</param>
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
		public static async Task<bool> DeleteAllByForeignKeyOrderIDAsync(OrderPrimaryKey pk, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
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
            var executionResult = await dh.ExecuteNonQueryAsync("gsp_OrderDetails_DeleteAllByForeignKeyOrderID", cancellationToken);
            wasExecutionSuccessful = executionResult.WasSuccessful;

            //Try Backup Server if Primary Server Succeeds (to keep both servers in sync)
            if (dh.ShouldUseBackupServer && wasExecutionSuccessful && dh.BackupConnectionString.Length != 0)
            {
                try
                {
                    bool backupExecutionState = false;

                    await dh.ExecuteNonQueryAsync("gsp_OrderDetails_DeleteAllByForeignKeyOrderID", CommandType.StoredProcedure, ConnectionState.CloseOnExit, cancellationToken);
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
		/// <param name="productID" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static OrderDetails SelectAllByForeignKeyProductID(int productID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new ProductPrimaryKey(productID);
			return SelectAllByForeignKeyProductID(pk, databaseHelper);
		}

		/// <summary>
		/// This method will get row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="productID" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<OrderDetails></returns>
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
		public static async Task<OrderDetails> SelectAllByForeignKeyProductIDAsync(int productID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new ProductPrimaryKey(productID);
			return await SelectAllByForeignKeyProductIDAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will count row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="productID" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static int SelectAllCountByForeignKeyProductID(int productID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new ProductPrimaryKey(productID);
			return SelectAllCountByForeignKeyProductID(pk, databaseHelper);
		}

		/// <summary>
		/// This method will count row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="productID" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<OrderDetails></returns>
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
		public static async Task<int> SelectAllCountByForeignKeyProductIDAsync(int productID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new ProductPrimaryKey(productID);
			return await SelectAllCountByForeignKeyProductIDAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="productID" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static OrderDetails SelectAllByForeignKeyProductIDPaged(int productID, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null)
		{
			var pk = new ProductPrimaryKey(productID);
			return SelectAllByForeignKeyProductIDPaged(pk, pageNumber, pageSize, orderByStatement, databaseHelper);
		}

		/// <summary>
		/// This method will get row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="productID" type="ProductsPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static async Task<OrderDetails> SelectAllByForeignKeyProductIDPagedAsync(int productID, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new ProductPrimaryKey(productID);
			return await SelectAllByForeignKeyProductIDPagedAsync(pk, pageNumber, pageSize, orderByStatement, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will delete row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="productID" type="ProductsPrimaryKey">Primary Key information based on which data is to be deleted.</param>
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
		public static bool DeleteAllByForeignKeyProductID(int productID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new ProductPrimaryKey(productID);
			return DeleteAllByForeignKeyProductID(pk, databaseHelper);
		}

		/// <summary>
		/// This method will delete row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="productID" type="ProductsPrimaryKey">Primary Key information based on which data is to be deleted.</param>
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
		public static async Task<bool> DeleteAllByForeignKeyProductIDAsync(int productID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new ProductPrimaryKey(productID);
			return await DeleteAllByForeignKeyProductIDAsync(pk, databaseHelper, cancellationToken);
		}



		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="orderID" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static OrderDetails SelectAllByForeignKeyOrderID(int orderID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new OrderPrimaryKey(orderID);
			return SelectAllByForeignKeyOrderID(pk, databaseHelper);
		}

		/// <summary>
		/// This method will get row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="orderID" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<OrderDetails></returns>
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
		public static async Task<OrderDetails> SelectAllByForeignKeyOrderIDAsync(int orderID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new OrderPrimaryKey(orderID);
			return await SelectAllByForeignKeyOrderIDAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will count row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="orderID" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static int SelectAllCountByForeignKeyOrderID(int orderID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new OrderPrimaryKey(orderID);
			return SelectAllCountByForeignKeyOrderID(pk, databaseHelper);
		}

		/// <summary>
		/// This method will count row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="orderID" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class Task<OrderDetails></returns>
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
		public static async Task<int> SelectAllCountByForeignKeyOrderIDAsync(int orderID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new OrderPrimaryKey(orderID);
			return await SelectAllCountByForeignKeyOrderIDAsync(pk, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will get row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="orderID" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static OrderDetails SelectAllByForeignKeyOrderIDPaged(int orderID, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null)
		{
			var pk = new OrderPrimaryKey(orderID);
			return SelectAllByForeignKeyOrderIDPaged(pk, pageNumber, pageSize, orderByStatement, databaseHelper);
		}

		/// <summary>
		/// This method will get row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="orderID" type="OrdersPrimaryKey">Primary Key information based on which data is to be fetched.</param>
		/// <param name="pageSize" type="int">Number of records returned.</param>
		/// <param name="pageNumber" type="int">The page number returned.</param>
		/// <param name="orderByStatement" type="string">The field value to number</param>
		/// <param name="cancellationToken" type="CancellationToken">CancellationToken to cancel the operation.</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>object of class OrderDetails</returns>
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
		public static async Task<OrderDetails> SelectAllByForeignKeyOrderIDPagedAsync(int orderID, int pageNumber, int pageSize, string orderByStatement, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new OrderPrimaryKey(orderID);
			return await SelectAllByForeignKeyOrderIDPagedAsync(pk, pageNumber, pageSize, orderByStatement, databaseHelper, cancellationToken);
		}

		/// <summary>
		/// This method will delete row(s) from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="orderID" type="OrdersPrimaryKey">Primary Key information based on which data is to be deleted.</param>
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
		public static bool DeleteAllByForeignKeyOrderID(int orderID, DatabaseHelper? databaseHelper = null)
		{
			var pk = new OrderPrimaryKey(orderID);
			return DeleteAllByForeignKeyOrderID(pk, databaseHelper);
		}

		/// <summary>
		/// This method will delete row(s) asynchronously from the database using the value of the field specified 
		/// along with the details of the child table.
		/// </summary>
		///
		/// <param name="orderID" type="OrdersPrimaryKey">Primary Key information based on which data is to be deleted.</param>
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
		public static async Task<bool> DeleteAllByForeignKeyOrderIDAsync(int orderID, DatabaseHelper? databaseHelper = null, CancellationToken cancellationToken = default)
		{
			var pk = new OrderPrimaryKey(orderID);
			return await DeleteAllByForeignKeyOrderIDAsync(pk, databaseHelper, cancellationToken);
		}

		#endregion	
		
		#region Methods (Private)
		
		/// <summary>
		/// Populates the fields of a single objects from the columns found in an open reader.
		/// </summary>
		/// <param name="obj" type="Order Details">Object of Order Details to populate</param>
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
		public static void PopulateObjectFromReader(OrderDetailBase obj,IDataReader rdr) 
		{

			int ord_OrderID = rdr.GetOrdinal(OrderDetailFields.OrderID);
			int ord_ProductID = rdr.GetOrdinal(OrderDetailFields.ProductID);
			int ord_UnitPrice = rdr.GetOrdinal(OrderDetailFields.UnitPrice);
			int ord_Quantity = rdr.GetOrdinal(OrderDetailFields.Quantity);
			int ord_Discount = rdr.GetOrdinal(OrderDetailFields.Discount);

			obj.OrderID = rdr.GetInt32(ord_OrderID);
			obj.ProductID = rdr.GetInt32(ord_ProductID);
			obj.UnitPrice = rdr.GetDecimal(ord_UnitPrice);
			obj.Quantity = rdr.GetInt16(ord_Quantity);
			obj.Discount = rdr.GetFloat(ord_Discount);

			obj.TakeSnapshot();
		}

		/// <summary>
		/// Populates the fields for multiple objects from the columns found in an open reader.
		/// </summary>
		///
		/// <param name="rdr" type="IDataReader">An object that implements the IDataReader interface</param>
		/// <param name="databaseHelper" type="DatabaseHelper">if needed DatabaseHelper object to use</param>
		///
		/// <returns>Object of OrderDetails</returns>
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
		public static OrderDetails PopulateObjectsFromReader(IDataReader rdr, DatabaseHelper? databaseHelper)
		{
			OrderDetails list = new OrderDetails();
			
			while (rdr.Read())
			{
				OrderDetail obj = new OrderDetail(databaseHelper);
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
		/// <returns>Object of OrderDetails</returns>
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
		public static async Task<OrderDetails> PopulateObjectsFromReaderAsync(DbDataReader rdr, DatabaseHelper? databaseHelper, CancellationToken cancellationToken)
		{
			OrderDetails list = new OrderDetails();
			
			while (await rdr.ReadAsync(cancellationToken))
			{
				OrderDetail obj = new OrderDetail(databaseHelper);
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
		/// <returns>Object of OrderDetails</returns>
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
		internal static OrderDetails PopulateObjectsFromReaderWithCheckingReader(IDataReader rdr, DatabaseHelper databaseHelper) 
		{

			OrderDetails list = new OrderDetails();
			
            if (rdr.Read())
			{
				OrderDetail obj = new OrderDetail(databaseHelper);
				PopulateObjectFromReader(obj, rdr);
				list.Add(obj);
				while (rdr.Read())
				{
					obj = new OrderDetail(databaseHelper);
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
		/// <returns>Object of OrderDetails</returns>
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
		internal static async Task<OrderDetails> PopulateObjectsFromReaderWithCheckingReaderAsync(DbDataReader rdr, DatabaseHelper databaseHelper, CancellationToken cancellationToken) 
		{

			OrderDetails list = new OrderDetails();
			
            if (await rdr.ReadAsync(cancellationToken))
			{
				OrderDetail obj = new OrderDetail(databaseHelper);
				PopulateObjectFromReader(obj, rdr);
				list.Add(obj);
				while (await rdr.ReadAsync(cancellationToken))
				{
					obj = new OrderDetail(databaseHelper);
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
		/// Populates the parameters for the Order Details table stored procedures.
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
			// Pass the value of '_orderID' as parameter 'OrderID' of the stored procedure.
			dh.AddParameter("@OrderID", _orderID);

			// Pass the value of '_productID' as parameter 'ProductID' of the stored procedure.
			dh.AddParameter("@ProductID", _productID);

			// Pass the value of '_unitPrice' as parameter 'UnitPrice' of the stored procedure.
			if(_unitPrice is not null)
			  dh.AddParameter("@UnitPrice", _unitPrice);
			else
			  dh.AddParameter("@UnitPrice", DBNull.Value );

			// Pass the value of '_quantity' as parameter 'Quantity' of the stored procedure.
			if(_quantity is not null)
			  dh.AddParameter("@Quantity", _quantity);
			else
			  dh.AddParameter("@Quantity", DBNull.Value );

			// Pass the value of '_discount' as parameter 'Discount' of the stored procedure.
			if(_discount is not null)
			  dh.AddParameter("@Discount", _discount);
			else
			  dh.AddParameter("@Discount", DBNull.Value );

			// The parameter '@dlgErrorCode' will contain the status after execution of the stored procedure.
			dh.AddParameter("@dlgErrorCode", -1, System.Data.ParameterDirection.Output);
			

		}

	
	#endregion

	}
}
