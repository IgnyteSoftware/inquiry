//
// Class	:	ShipperPrimaryKey.cs
// Author	:  	Inquiry © 2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:12 PM
//

using System;
using System.Collections.Specialized;

namespace Inquiry.Benchmarks.DLG
{
	public class ShipperPrimaryKey
	{

	#region Class Level Variables
			private int            	_shipperID               	= 0;
	#endregion

	#region Constants

	#endregion

	#region Constructors / Destructors

		/// <summary>
		/// Constructor setting values for all fields
		/// </summary>
		public ShipperPrimaryKey(int shipperID) 
		{
	
			
			this._shipperID = shipperID;

		}

		#endregion

	#region Properties

		
		/// <summary>
		/// This property is mapped to the "ShipperID" field.  Mandatory.
		/// </summary>
		[Trackable]
		public int ShipperID
		{
			get 
			{ 
				return _shipperID;
			}
			set 
			{
			
				_shipperID = value; 
			}
		}
      		
		//This property is related to the table name that exist in database
		
		public static string TableName
		{
			get 
			{ 
				  return "Shippers";
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
			
			nvc.Add("ShipperID",_shipperID.ToString());
			return nvc;
			
		}

		#endregion	
		
	#region Methods (Private)

	#endregion

	}
}
