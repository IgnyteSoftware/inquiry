//
// Enum		:	EnumTypeOperation.cs
// Author	:  	Inquiry © 2011 (DLG 6.0.1)
// Date		:	6/4/2026 10:07:11 PM
//

using System.Collections;

namespace Inquiry.Benchmarks.DLG
{
	/// <summary>
	/// Enumeration of the types of operations
	/// </summary>
	public enum TypeOperation
	{
		Like,
		Less,
		Greater,
		Equal,
		NotEqual
	}
	
  public enum DMLOperation
    {
        Insert,
        Update,
        Delete,
		Upsert
    }
	
	/// <summary>
	/// This class returns a string that match the enum TypeOperation
	/// </summary>
	public static class OperationCollection
	{
		public static readonly Hashtable Operation = new Hashtable() { { 0, "Like" }, { 1, "<" }, { 2, ">" }, { 3, "=" }, { 4, "<>" } };
		public const string SEPARATOR = "\n";
	}
}

