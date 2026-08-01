//
// Class	:	EmployeePrimaryKey.cs
// Author	:  	Inquiry © 2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:12 PM
//

using System;
using System.Collections.Specialized;

namespace Inquiry.Benchmarks.DLG
{
	public class EmployeePrimaryKey
	{

	#region Class Level Variables
			private int            	_employeeID              	= 0;
	#endregion

	#region Constants

	#endregion

	#region Constructors / Destructors

		/// <summary>
		/// Constructor setting values for all fields
		/// </summary>
		public EmployeePrimaryKey(int employeeID) 
		{
	
			
			this._employeeID = employeeID;

		}

		#endregion

	#region Properties

		
		/// <summary>
		/// This property is mapped to the "EmployeeID" field.  Mandatory.
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
			return nvc;
			
		}

		#endregion	
		
	#region Methods (Private)

	#endregion

	}
}
