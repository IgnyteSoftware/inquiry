//
// Class	:	Employee.cs
// Author	:  	Inquiry © 2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:12 PM
//

using System;
namespace Inquiry.Benchmarks.DLG
{
	
	/// <summary>
	/// Data access class for the "Employees" table.
	/// </summary>
	[Serializable]
	public class Employee : EmployeeBase
	{
	
		#region Class Level Variables

		#endregion
		
		#region Constants
		
		#endregion

		#region Constructors / Destructors 
		
		public Employee() : base()
		{
		}
    
    public Employee(DatabaseHelper? databaseHelper) : base(databaseHelper)
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
