//
// Class	:	ProductPrimaryKey.cs
// Author	:  	Inquiry © 2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:12 PM
//

using System;
using System.Collections.Specialized;

namespace Inquiry.Benchmarks.DLG
{
	public class ProductPrimaryKey
	{

	#region Class Level Variables
			private int            	_productID               	= 0;
	#endregion

	#region Constants

	#endregion

	#region Constructors / Destructors

		/// <summary>
		/// Constructor setting values for all fields
		/// </summary>
		public ProductPrimaryKey(int productID) 
		{
	
			
			this._productID = productID;

		}

		#endregion

	#region Properties

		
		/// <summary>
		/// This property is mapped to the "ProductID" field.  Mandatory.
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
				  return "Products";
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
			
			nvc.Add("ProductID",_productID.ToString());
			return nvc;
			
		}

		#endregion	
		
	#region Methods (Private)

	#endregion

	}
}
