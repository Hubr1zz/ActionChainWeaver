using UnityEngine;

namespace CardGame.ActionQueue.Examples
{
    public sealed class Combatant : IReactorEntity
    {
        public Combatant(string name, int maxHp)
        {
            Name = name;
            MaxHp = maxHp;
            Hp = maxHp;
        }

        public string Name { get; }
        public int MaxHp { get; }
        public int Hp { get; private set; }
        public string ReactorName => Name;

        public void TakeDamage(int amount)
        {
            Hp = Mathf.Max(0, Hp - Mathf.Max(0, amount));
        }

        public void Heal(int amount)
        {
            Hp = Mathf.Min(MaxHp, Hp + Mathf.Max(0, amount));
        }

        public override string ToString() => $"{Name} HP={Hp}/{MaxHp}";
    }

    public sealed class DeckState : IReactorEntity
    {
        public DeckState(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public int CardsInHand { get; private set; }
        public string ReactorName => Name;

        public void Draw(int amount)
        {
            CardsInHand += Mathf.Max(0, amount);
        }
    }
}
