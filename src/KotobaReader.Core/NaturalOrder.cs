namespace KotobaReader.Core;

public sealed class NaturalOrder : IComparer<string>
{
    public static NaturalOrder Instance { get; } = new();
    public int Compare(string? left, string? right)
    {
        if (left is null || right is null) return string.CompareOrdinal(left, right);
        int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsAsciiDigit(left[i]) && char.IsAsciiDigit(right[j]))
            {
                int a = i, b = j;
                while (i < left.Length && char.IsAsciiDigit(left[i])) i++;
                while (j < right.Length && char.IsAsciiDigit(right[j])) j++;
                var x = left[a..i].TrimStart('0');
                var y = right[b..j].TrimStart('0');
                var result = x.Length.CompareTo(y.Length);
                if (result == 0) result = string.CompareOrdinal(x, y);
                if (result != 0) return result;
            }
            else
            {
                var result = char.ToUpperInvariant(left[i++]).CompareTo(char.ToUpperInvariant(right[j++]));
                if (result != 0) return result;
            }
        }
        var remaining = (left.Length - i).CompareTo(right.Length - j);
        return remaining != 0 ? remaining : string.CompareOrdinal(left, right);
    }
}
