using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hypersonic.Core.Models;

public class SuggestionItem
{
    public string Text { get; set; } = null!;
    public string ImageUrl { get; set; } = null!; // Or you can use an ImageSource if you're loading images from a resource.
}
