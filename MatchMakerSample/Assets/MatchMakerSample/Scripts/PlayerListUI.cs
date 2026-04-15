using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace MatchMakerSample.Client
{
    // Manages the UI for a list of player names. Each player has a name and an id so players can be removed by their
    // id. The game object should have a single child with a TMP_Text component that gets cloned for each player name,
    // and a VerticalLayoutGroup or some other component that controls the positions of the children.
    public class PlayerListUI : MonoBehaviour
    {
        private TMP_Text m_template;

        // Map of player ids to the TMP_Text used to display their name. Used for removing players by id.
        private Dictionary<uint, TMP_Text> m_playerNameMap = new Dictionary<uint, TMP_Text> ();

        // The number of players in the list.
        public int Count
        {
            get { return m_playerNameMap.Count; }
        }

        private void Awake()
        {
            // Set the template to the TMP_Text from the first child, and disable its game object.
            m_template = transform.GetChild(0).GetComponent<TMP_Text>();
            m_template.gameObject.SetActive(false);
        }

        // Adds a player to the list.
        public void Add(uint playerId, string name)
        {
            if (m_playerNameMap.ContainsKey(playerId))
            {
                throw new ArgumentException("Duplicate playerId " + playerId);
            }
            TMP_Text text = Instantiate(m_template, transform);
            text.text = name;
            text.gameObject.SetActive(true);
            m_playerNameMap[playerId] = text;
        }

        // Removes the player with the given id.
        public void Remove(uint playerId)
        {
            TMP_Text text;
            if (m_playerNameMap.Remove(playerId, out text))
            {
                Destroy(text.gameObject);
            }
        }

        // Clears the list.
        public void Clear()
        {
            for (int i = 1; i < transform.childCount; i++)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
            m_playerNameMap.Clear();
        }
    }
}