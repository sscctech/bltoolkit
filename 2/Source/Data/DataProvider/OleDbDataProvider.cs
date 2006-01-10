using System;
using System.Data;
using System.Data.Common;
using System.Data.OleDb;

namespace BLToolkit.Data.DataProvider
{
	/// <summary>
	/// Implements access to the Data Provider for OLE DB.
	/// </summary>
	/// <remarks>
	/// See the <see cref="DbManager.AddDataProvider"/> method to find an example.
	/// </remarks>
	/// <seealso cref="DbManager.AddDataProvider">AddDataManager Method</seealso>
	public class OleDbDataProvider : IDataProvider
	{
		/// <summary>
		/// Creates the database connection object.
		/// </summary>
		/// <remarks>
		/// See the <see cref="DbManager.AddDataProvider"/> method to find an example.
		/// </remarks>
		/// <seealso cref="DbManager.AddDataProvider">AddDataManager Method</seealso>
		/// <returns>The database connection object.</returns>
		public virtual IDbConnection CreateConnectionObject()
		{
			return new OleDbConnection();
		}

		/// <summary>
		/// Creates the data adapter object.
		/// </summary>
		/// <remarks>
		/// See the <see cref="DbManager.AddDataProvider"/> method to find an example.
		/// </remarks>
		/// <seealso cref="DbManager.AddDataProvider">AddDataManager Method</seealso>
		/// <returns>A data adapter object.</returns>
		public virtual DbDataAdapter CreateDataAdapterObject()
		{
			return new OleDbDataAdapter();
		}

		/// <summary>
		/// Populates the specified IDbCommand object's Parameters collection with 
		/// parameter information for the stored procedure specified in the IDbCommand.
		/// </summary>
		/// <remarks>
		/// See the <see cref="DbManager.AddDataProvider"/> method to find an example.
		/// </remarks>
		/// <seealso cref="DbManager.AddDataProvider">AddDataManager Method</seealso>
		/// <param name="command">The IDbCommand referencing the stored procedure for which the parameter information is to be derived. The derived parameters will be populated into the Parameters of this command.</param>
		public virtual bool DeriveParameters(IDbCommand command)
		{
			if (command.Connection.ConnectionString.IndexOf("Jet.OLEDB") > 0)
				return false;

			OleDbCommandBuilder.DeriveParameters((OleDbCommand)command);

			return true;
		}

		public virtual string GetParameterName(string name)
		{
			return "@" + name;
		}

		public virtual string GetNameFromParameter(string parameterName)
		{
			return parameterName != null && parameterName.Length > 0 && parameterName[0] == '@'?
				parameterName.Substring(1):
				parameterName;
		}

		/// <summary>
		/// Returns connection type.
		/// </summary>
		/// <remarks>
		/// See the <see cref="DbManager.AddDataProvider"/> method to find an example.
		/// </remarks>
		/// <seealso cref="DbManager.AddDataProvider">AddDataManager Method</seealso>
		/// <value>An instance of the <see cref="Type"/> class.</value>
		public virtual Type ConnectionType
		{
			get { return typeof(OleDbConnection); }
		}

		/// <summary>
		/// Returns the data provider name.
		/// </summary>
		/// <remarks>
		/// See the <see cref="DbManager.AddDataProvider"/> method to find an example.
		/// </remarks>
		/// <seealso cref="DbManager.AddDataProvider">AddDataProvider Method</seealso>
		/// <value>Data provider name.</value>
		public virtual string Name
		{
			get { return "OleDb"; }
		}
	}
}
