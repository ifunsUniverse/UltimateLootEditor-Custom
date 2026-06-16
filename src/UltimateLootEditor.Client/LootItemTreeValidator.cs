using System.Collections.Generic;
using System.Linq;

namespace ULE.SpawnEditor
{
    internal static class LootItemTreeValidator
    {
        public static bool HasMissingTemplates(LootItemNode root)
        {
            if (root == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(root.Tpl) && !TplCache.TemplateExists(root.Tpl))
            {
                return true;
            }

            return HasMissingTemplates(root.Children);
        }

        private static bool HasMissingTemplates(IEnumerable<LootItemNode> nodes)
        {
            if (nodes == null)
            {
                return false;
            }

            return nodes.Any(HasMissingTemplates);
        }
    }
}
