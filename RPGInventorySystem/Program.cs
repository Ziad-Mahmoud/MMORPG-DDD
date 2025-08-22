using System;
using System.Collections.Generic;
using System.Linq;

namespace RPGInventorySystem.Refactored
{
    // ===============================================
    // PRINCIPLE 1: Separate Data from Behavior
    // ===============================================

    // Pure data structures (no business logic)
    public class ItemDefinition
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public Weight Weight { get; init; }  // Value object!
        public int MaxStackSize { get; init; }
        public int RequiredLevel { get; init; }
        public ItemType Type { get; init; }
    }

    // Value Object - Weight has its own rules and behavior
    public struct Weight
    {
        public float Value { get; }

        public Weight(float value)
        {
            if (value < 0)
                throw new ArgumentException("Weight cannot be negative");
            Value = value;
        }

        public static Weight operator +(Weight a, Weight b)
            => new Weight(a.Value + b.Value);

        public static Weight operator *(Weight w, int quantity)
            => new Weight(w.Value * quantity);

        public override string ToString() => $"{Value:F1}kg";
    }

    public enum ItemType
    {
        Weapon, Armor, Consumable, Material, Misc
    }

    // ===============================================
    // PRINCIPLE 2: Separate Rules from Data
    // ===============================================

    // Each rule is its own class - easy to test, modify, and understand
    public interface IInventoryRule
    {
        RuleResult CanAddItem(Player player, ItemDefinition item, int quantity);
    }

    public class RuleResult
    {
        public bool IsAllowed { get; init; }
        public string Reason { get; init; }
        public int? AllowedQuantity { get; init; }

        public static RuleResult Allow()
            => new() { IsAllowed = true };

        public static RuleResult Deny(string reason)
            => new() { IsAllowed = false, Reason = reason };
    }

    // Rule 1: Level Requirements
    public class LevelRequirementRule : IInventoryRule
    {
        public RuleResult CanAddItem(Player player, ItemDefinition item, int quantity)
        {
            if (item.RequiredLevel > player.Level)
            {
                return RuleResult.Deny(
                    $"Requires level {item.RequiredLevel} (you are level {player.Level})"
                );
            }
            return RuleResult.Allow();
        }
    }

    // Rule 2: Weight Limits
    public class WeightLimitRule : IInventoryRule
    {
        public RuleResult CanAddItem(Player player, ItemDefinition item, int quantity)
        {
            var inventory = player.GetInventory();
            var currentWeight = inventory.CalculateTotalWeight();
            var additionalWeight = item.Weight * quantity;

            if (currentWeight.Value + additionalWeight.Value > player.MaxCarryWeight)
            {
                var availableWeight = player.MaxCarryWeight - currentWeight.Value;
                var maxCanCarry = (int)(availableWeight / item.Weight.Value);

                if (maxCanCarry == 0)
                {
                    return RuleResult.Deny(
                        $"Too heavy! Item weighs {item.Weight}, carrying {currentWeight}/{player.MaxCarryWeight}kg"
                    );
                }

                return new RuleResult
                {
                    IsAllowed = false,
                    Reason = $"Weight limit allows only {maxCanCarry} items",
                    AllowedQuantity = maxCanCarry
                };
            }

            return RuleResult.Allow();
        }
    }

    // Rule 3: Stack Limits
    public class StackLimitRule : IInventoryRule
    {
        public RuleResult CanAddItem(Player player, ItemDefinition item, int quantity)
        {
            var inventory = player.GetInventory();
            var existingStack = inventory.FindStack(item.Id);

            if (existingStack != null)
            {
                var spaceInStack = item.MaxStackSize - existingStack.Quantity;
                if (spaceInStack < quantity)
                {
                    return new RuleResult
                    {
                        IsAllowed = false,
                        Reason = $"Stack limit: can only add {spaceInStack} more",
                        AllowedQuantity = spaceInStack
                    };
                }
            }
            else if (quantity > item.MaxStackSize)
            {
                return new RuleResult
                {
                    IsAllowed = false,
                    Reason = $"Max stack size is {item.MaxStackSize}",
                    AllowedQuantity = item.MaxStackSize
                };
            }

            return RuleResult.Allow();
        }
    }

    // ===============================================
    // PRINCIPLE 3: Separate Complex Operations
    // ===============================================

    // Inventory is its own concept with its own responsibilities
    public class Inventory
    {
        private readonly List<ItemStack> stacks = new();

        public IReadOnlyList<ItemStack> Stacks => stacks.AsReadOnly();

        public Weight CalculateTotalWeight()
        {
            return stacks.Aggregate(
                new Weight(0),
                (total, stack) => total + stack.TotalWeight
            );
        }

        public ItemStack FindStack(string itemId)
        {
            return stacks.FirstOrDefault(s => s.ItemDef.Id == itemId);
        }

        public void AddItem(ItemDefinition item, int quantity)
        {
            var existingStack = FindStack(item.Id);

            if (existingStack != null)
            {
                existingStack.Add(quantity);
            }
            else
            {
                stacks.Add(new ItemStack(item, quantity));
            }
        }

        public bool RemoveItem(string itemId, int quantity)
        {
            var stack = FindStack(itemId);
            if (stack == null || stack.Quantity < quantity)
                return false;

            stack.Remove(quantity);

            if (stack.Quantity == 0)
                stacks.Remove(stack);

            return true;
        }
    }

    public class ItemStack
    {
        public ItemDefinition ItemDef { get; }
        public int Quantity { get; private set; }

        public Weight TotalWeight => ItemDef.Weight * Quantity;

        public ItemStack(ItemDefinition itemDef, int quantity = 1)
        {
            ItemDef = itemDef;
            Quantity = quantity;
        }

        public void Add(int amount) => Quantity += amount;
        public void Remove(int amount) => Quantity = Math.Max(0, Quantity - amount);
    }

    // ===============================================
    // PRINCIPLE 4: Orchestration Layer
    // ===============================================

    // This class ONLY orchestrates - it doesn't contain business logic
    public class InventoryService
    {
        private readonly List<IInventoryRule> rules;

        public InventoryService()
        {
            // All rules in one place, easy to add/remove
            rules = new List<IInventoryRule>
            {
                new LevelRequirementRule(),
                new WeightLimitRule(),
                new StackLimitRule()
            };
        }

        public AddItemResult TryAddItem(Player player, ItemDefinition item, int quantity)
        {
            // Check all rules
            foreach (var rule in rules)
            {
                var result = rule.CanAddItem(player, item, quantity);
                if (!result.IsAllowed)
                {
                    return new AddItemResult
                    {
                        Success = false,
                        Message = result.Reason,
                        QuantityAdded = 0,
                        QuantityAllowed = result.AllowedQuantity
                    };
                }
            }

            // All rules passed, add the item
            player.GetInventory().AddItem(item, quantity);

            return new AddItemResult
            {
                Success = true,
                Message = $"Added {quantity} {item.Name}(s)",
                QuantityAdded = quantity
            };
        }

        // Easy to add new features without modifying existing code
        public void AddRule(IInventoryRule rule)
        {
            rules.Add(rule);
        }
    }

    public class AddItemResult
    {
        public bool Success { get; init; }
        public string Message { get; init; }
        public int QuantityAdded { get; init; }
        public int? QuantityAllowed { get; init; }
    }

    // ===============================================
    // PRINCIPLE 5: Player is simple again!
    // ===============================================

    public class Player
    {
        // Player ONLY knows player-specific data
        public string Name { get; init; }
        public int Level { get; set; }
        public float MaxCarryWeight { get; init; }

        private readonly Inventory inventory = new();

        // Player doesn't manage inventory logic, just provides access
        public Inventory GetInventory() => inventory;

        public Player(string name, int level = 1, float maxCarryWeight = 100f)
        {
            Name = name;
            Level = level;
            MaxCarryWeight = maxCarryWeight;
        }
    }

    // ===============================================
    // DEMO: See how clean this is to use and extend
    // ===============================================

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== REFACTORED INVENTORY SYSTEM ===\n");

            // Setup
            var player = new Player("Hero", level: 5, maxCarryWeight: 50f);
            var inventoryService = new InventoryService();

            // Items
            var sword = new ItemDefinition
            {
                Id = "sword_iron",
                Name = "Iron Sword",
                Weight = new Weight(3.5f),
                MaxStackSize = 1,
                RequiredLevel = 5,
                Type = ItemType.Weapon
            };

            var potion = new ItemDefinition
            {
                Id = "potion_health",
                Name = "Health Potion",
                Weight = new Weight(0.5f),
                MaxStackSize = 20,
                RequiredLevel = 1,
                Type = ItemType.Consumable
            };

            // Test adding items
            var result = inventoryService.TryAddItem(player, sword, 1);
            Console.WriteLine($"Adding sword: {result.Message}");

            result = inventoryService.TryAddItem(player, potion, 15);
            Console.WriteLine($"Adding 15 potions: {result.Message}");

            // NOW LOOK HOW EASY IT IS TO ADD NEW FEATURES!

            // Want VIP weekend bonus? Just add a new rule:
            Console.WriteLine("\n=== Adding VIP Weekend Rule ===");
            inventoryService.AddRule(new VIPWeekendRule());

            // Want different bag types? Create a bag manager:
            // var bagManager = new BagManager(player);
            // bagManager.EquipBag(new MerchantBag());

            // Want auction house? Completely separate system:
            // var auctionHouse = new AuctionHouse();
            // auctionHouse.CreateListing(player, item, price);

            // Each system is independent and testable!

            DisplayInventory(player);
        }

        static void DisplayInventory(Player player)
        {
            var inv = player.GetInventory();
            Console.WriteLine($"\n{player.Name}'s Inventory:");
            Console.WriteLine($"Weight: {inv.CalculateTotalWeight()}/{player.MaxCarryWeight}kg");

            foreach (var stack in inv.Stacks)
            {
                Console.WriteLine($"- {stack.ItemDef.Name} x{stack.Quantity}");
            }
        }
    }

    // Example of how easy it is to add new rules now!
    public class VIPWeekendRule : IInventoryRule
    {
        public RuleResult CanAddItem(Player player, ItemDefinition item, int quantity)
        {
            bool isWeekend = DateTime.Now.DayOfWeek == DayOfWeek.Saturday ||
                             DateTime.Now.DayOfWeek == DayOfWeek.Sunday;
            bool isVIP = player.Level >= 20;

            if (isWeekend && isVIP)
            {
                // VIP weekend bonus already factored into weight limit
                // This is just for demonstration
                Console.WriteLine("[VIP Weekend Bonus Active!]");
            }

            return RuleResult.Allow();
        }
    }
}

/*
 * === WHAT WE ACHIEVED ===
 * 
 * 1. SINGLE RESPONSIBILITY:
 *    - Player: Knows player data
 *    - Inventory: Manages item storage
 *    - Rules: Each handles one validation
 *    - Service: Orchestrates the process
 * 
 * 2. OPEN/CLOSED PRINCIPLE:
 *    - Add new rules WITHOUT changing existing code
 *    - New features don't break old ones
 * 
 * 3. TESTABILITY:
 *    - Test weight limits without a full player
 *    - Test each rule in isolation
 *    - Mock dependencies easily
 * 
 * 4. MAINTAINABILITY:
 *    - Find level requirement logic? Check LevelRequirementRule
 *    - Fix weight calculation? Only touch WeightLimitRule
 *    - Add trading? Create TradingService, don't touch inventory
 * 
 * THIS is the foundation of Domain-Driven Design!
 */