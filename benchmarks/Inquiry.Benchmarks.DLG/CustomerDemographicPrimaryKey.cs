//
// Class	:	CustomerDemographicPrimaryKey.cs
// Author	:  	Inquiry © 2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:11 PM
//

using System;
using System.Collections.Specialized;

namespace Inquiry.Benchmarks.DLG
{
	public class CustomerDemographicPrimaryKey
	{

	#region Class Level Variables
			private string         	_customerTypeID          	= string.Empty;
	#endregion

	#region Constants

	#endregion

	#region Constructors / Destructors

		/// <summary>
		/// Constructor setting values for all fields
		/// </summary>
		public CustomerDemographicPrimaryKey(string customerTypeID) 
		{
	
			
			this._customerTypeID = customerTypeID;

		}

		#endregion

	#region Properties

		
		/// <summary>
		/// This property is mapped to the "CustomerTypeID" field. Length must be between 0 and 10 characters. Mandatory.
		/// </summary>
		[Trackable]
		public string CustomerTypeID
		{
			get 
			{ 
				return _customerTypeID.Trim();
			}
			set 
			{
				
				if (value is null)
					throw new ArgumentNullException("value", "Value is null.");
				
				if (value is not null && value.Length > 10)
					throw new ArgumentException("CustomerTypeID length must be between 0 and 10 characters.");
				
				if (value is not null)
				{		           
					_customerTypeID = value.Trim(); 
				}
			}
		}
      		
		//This property is related to the table name that exist in database
		
		public static string TableName
		{
			get 
			{ 
				  return "CustomerDemographics";
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
		/// DLGenerator			6/4/2026 10:07:11 PM				Created function
		/// 
		/// </RevisionHistory>
		///
		/// </remarks>
		///
		public NameValueCollection GetKeysAndValues() 
		{
			NameValueCollection nvc=new NameValueCollection();
			
			nvc.Add("CustomerTypeID",_customerTypeID.ToString());
			return nvc;
			
		}

		#endregion	
		
	#region Methods (Private)

	#endregion

	}
}
