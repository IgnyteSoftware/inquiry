//
// Class	:	OrderDetailPrimaryKey.cs
// Author	:  	Inquiry © 2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:12 PM
//

using System;
using System.Collections.Specialized;

namespace Inquiry.Benchmarks.DLG
{
	public class OrderDetailPrimaryKey
	{

	#region Class Level Variables
			private int            	_orderID                 	= 0;
		private int            	_productID               	= 0;
	#endregion

	#region Constants

	#endregion

	#region Constructors / Destructors

		/// <summary>
		/// Constructor setting values for all fields
		/// </summary>
		public OrderDetailPrimaryKey(int orderID,int productID) 
		{
	
			
			this._orderID = orderID;
			
			this._productID = productID;

		}

		#endregion

	#region Properties

		
		/// <summary>
		/// The foreign key connected with another persistent object. Mandatory.
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
			
				_orderID = value; 
			}
		}
      

		
		/// <summary>
		/// The foreign key connected with another persistent object. Mandatory.
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
			
				_productID = value; 
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
			
			nvc.Add("OrderID",_orderID.ToString());
			nvc.Add("ProductID",_productID.ToString());
			return nvc;
			
		}

		#endregion	
		
	#region Methods (Private)

	#endregion

	}
}
