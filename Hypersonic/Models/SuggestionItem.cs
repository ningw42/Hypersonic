using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hypersonic.Models;

public class SuggestionItem
{
    public string Text { get; set; }
    public string ImageUrl { get; set; } // Or you can use an ImageSource if you're loading images from a resource.
}
