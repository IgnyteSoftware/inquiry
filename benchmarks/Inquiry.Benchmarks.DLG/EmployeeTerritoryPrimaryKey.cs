//
// Class	:	EmployeeTerritoryPrimaryKey.cs
// Author	:  	Inquiry © 2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:12 PM
//

using System;
using System.Collections.Specialized;

namespace Inquiry.Benchmarks.DLG
{
	public class EmployeeTerritoryPrimaryKey
	{

	#region Class Level Variables
			private int            	_employeeID              	= 0;
		private string         	_territoryID             	= string.Empty;
	#endregion

	#region Constants

	#endregion

	#region Constructors / Destructors

		/// <summary>
		/// Constructor setting values for all fields
		/// </summary>
		public EmployeeTerritoryPrimaryKey(int employeeID,string territoryID) 
		{
	
			
			this._employeeID = employeeID;
			
			this._territoryID = territoryID;

		}

		#endregion

	#region Properties

		
		/// <summary>
		/// The foreign key connected with another persistent object. Mandatory.
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
			
				_employeeID = value; 
			}
		}
      

		
		/// <summary>
		/// The foreign key connected with another persistent object. Mandatory.
		/// </summary>
		[Trackable]
		public string TerritoryID
		{
			get 
			{ 
				return _territoryID.Trim();
			}
			set 
			{
				
				if (value is null)
					throw new ArgumentNullException("value", "Value is null.");
				
				if (value is not null && value.Length > 40)
					throw new ArgumentException("TerritoryID length must be between 0 and 40 characters.");
				
				if (value is not null)
				{		           
					_territoryID = value.Trim(); 
				}
			}
		}
      		
		//This property is related to the table name that exist in database
		
		public static string TableName
		{
			get 
			{ 
				  return "EmployeeTerritories";
			}
		}
      

		#endregion

	#region Methods (Public)

		/// <summary>
		/// Method to get the list of fields and their values
		/// </summary>
		///
		/// <returns>Name value collection containing the fields and the values</returns>
		///
		/// <remarks>
		///
		/// <RevisionHistory>
		/// Author				Date			                    Description
		/// DLGenerator			6/4/2026 10:07:12 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public NameValueCollection GetKeysAndValues() 
		{
			NameValueCollection nvc=new NameValueCollection();
			
			nvc.Add("EmployeeID",_employeeID.ToString());
			nvc.Add("TerritoryID",_territoryID.ToString());
			return nvc;
			
		}

		#endregion	
		
	#region Methods (Private)

	#endregion

	}
}
