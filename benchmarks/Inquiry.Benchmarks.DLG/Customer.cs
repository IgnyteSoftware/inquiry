//
// Class	:	Customer.cs
// Author	:  	Inquiry © 2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:11 PM
//

using System;
namespace Inquiry.Benchmarks.DLG
{
	
	/// <summary>
	/// Data access class for the "Customers" table.
	/// </summary>
	[Serializable]
	public class Customer : CustomerBase
	{
	
		#region Class Level Variables

		#endregion
		
		#region Constants
		
		#endregion

		#region Constructors / Destructors 
		
		public Customer() : base()
		{
		}
    
    public Customer(DatabaseHelper? databaseHelper) : base(databaseHelper)
		{
		}

		#endregion

		#region Properties

		#endregion

		#region Methods (Public)

		#endregion
		
		#region Methods (Private)

		#endregion

	}
	
}
