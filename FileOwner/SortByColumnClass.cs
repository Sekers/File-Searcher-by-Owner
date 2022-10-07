using System.Collections;
using System.Windows.Forms;
using System; //added in to use the Convert command for sorting numerically


//
// The column sorting components were created by following MS article http://support.microsoft.com/kb/319401
// Modified slightly to allow sorting by number based off of http://www.experts-exchange.com/Programming/Languages/C_Sharp/Q_21124982.html
//

/// <summary>
/// This class is an implementation of the 'IComparer' interface.
/// </summary>
public class ListViewColumnSorter : IComparer
{
    /// <summary>
    /// Specifies the column to be sorted
    /// </summary>
    private int ColumnToSort;
    /// <summary>
    /// Specifies the order in which to sort (i.e. 'Ascending').
    /// </summary>
    private SortOrder OrderOfSort;
    /// <summary>
    /// Case insensitive comparer object
    /// </summary>
    //private CaseInsensitiveComparer ObjectCompare; //Commented out to use custom sort in line blow this one
    private MyCompare ObjectCompare; //Added to allow sorting numerically

    /// <summary>
    /// Class constructor.  Initializes various elements
    /// </summary>
    public ListViewColumnSorter()
    {
        // Initialize the column to '0'
        ColumnToSort = 0;

        // Initialize the sort order to 'none'
        OrderOfSort = SortOrder.None;

        // Initialize the CaseInsensitiveComparer object
        // ObjectCompare = new CaseInsensitiveComparer(); //Commented out to use custom sort in line blow this one

        ObjectCompare = new MyCompare(); //Added to allow sorting numerically
    }

    /// <summary>
    /// This method is inherited from the IComparer interface.  It compares the two objects passed using a case insensitive comparison.
    /// </summary>
    /// <param name="x">First object to be compared</param>
    /// <param name="y">Second object to be compared</param>
    /// <returns>The result of the comparison. "0" if equal, negative if 'x' is less than 'y' and positive if 'x' is greater than 'y'</returns>
    public int Compare(object x, object y)
    {
        int compareResult;
        ListViewItem listviewX, listviewY;

        // Cast the objects to be compared to ListViewItem objects
        listviewX = (ListViewItem)x;
        listviewY = (ListViewItem)y;

        // Compare the two items
        compareResult = ObjectCompare.Compare(listviewX.SubItems[ColumnToSort].Text, listviewY.SubItems[ColumnToSort].Text);   

        // Calculate correct return value based on object comparison
        if (OrderOfSort == SortOrder.Ascending)
        {
            // Ascending sort is selected, return normal result of compare operation
            return compareResult;
        }
        else if (OrderOfSort == SortOrder.Descending)
        {
            // Descending sort is selected, return negative result of compare operation
            return (-compareResult);
        }
        else
        {
            // Return '0' to indicate they are equal
            return 0;
        }
    }

    /// <summary>
    /// Gets or sets the number of the column to which to apply the sorting operation (Defaults to '0').
    /// </summary>
    public int SortColumn
    {
        set
        {
            ColumnToSort = value;
        }
        get
        {
            return ColumnToSort;
        }
    }

    /// <summary>
    /// Gets or sets the order of sorting to apply (for example, 'Ascending' or 'Descending').
    /// </summary>
    public SortOrder Order
    {
        set
        {
            OrderOfSort = value;
        }
        get
        {
            return OrderOfSort;
        }
    }

}

//Added to sort numerically (based on http://www.experts-exchange.com/Programming/Languages/C_Sharp/Q_21124982.html)
public class MyCompare : System.Collections.IComparer
{
    private System.Collections.CaseInsensitiveComparer ciComparer = new System.Collections.CaseInsensitiveComparer();
    private System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(@"^[1-9]\d*(\.\d+)?$"); // matches number and decimal point (does not match commas). I changed this regex to meet my needs

    public int Compare(object x, object y) // Assuming x, y are Strings (from ListView.Text)
    {
        // If the value is a number, parse as double and compare
        if (regex.Match((string)x.ToString().Replace(",", "")).Success && regex.Match((string)y.ToString().Replace(",", "")).Success) // Modified to strip out commas in filesize so it knows it's a number because regex doesn't check for that
        {
            x = x.ToString().Replace(",", ""); //strip out the commas for the compare
            y = y.ToString().Replace(",", ""); //strip out the commas for the compare
            return double.Parse(x.ToString()).CompareTo(double.Parse(y.ToString()));
        }
        else // not a number so just compare normally
        {
            return ciComparer.Compare(x, y);
        }
    }
}