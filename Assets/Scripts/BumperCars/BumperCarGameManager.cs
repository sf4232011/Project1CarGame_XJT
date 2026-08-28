using TMPro;
using UnityEngine;

namespace BumperCars
{
    public sealed class BumperCarGameManager : MonoBehaviour
    {
        [Header("Players")]
        [SerializeField] private BumperCarController player1Controller;
        [SerializeField] private BumperCarHealth player1Health;
        [SerializeField] private BumperCarController player2Controller;
        [SerializeField] private BumperCarHealth player2Health;

        [Header("Cameras")]
        [SerializeField] private Camera player1Camera;
        [SerializeField] private Camera player2Camera;

        [Header("UI")]
        [SerializeField] private BumperCarHudPanel player1Hud;
        [SerializeField] private BumperCarHudPanel player2Hud;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text resultText;

        [Header("Rules")]
        [SerializeField] private float matchDuration = 60f;

        private float remainingTime;
        private bool gameEnded;

        private void Awake()
        {
            ConfigureSplitScreen();
            remainingTime = matchDuration;

            if (player1Hud != null)
            {
                player1Hud.Bind(player1Health, "PLAYER 1");
            }

            if (player2Hud != null)
            {
                player2Hud.Bind(player2Health, "PLAYER 2");
            }

            if (resultText != null)
            {
                resultText.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (player1Health != null)
            {
                player1Health.Defeated += OnPlayerDefeated;
            }

            if (player2Health != null)
            {
                player2Health.Defeated += OnPlayerDefeated;
            }
        }

        private void OnDisable()
        {
            if (player1Health != null)
            {
                player1Health.Defeated -= OnPlayerDefeated;
            }

            if (player2Health != null)
            {
                player2Health.Defeated -= OnPlayerDefeated;
            }
        }

        private void Update()
        {
            if (gameEnded)
            {
                return;
            }

            remainingTime = Mathf.Max(0f, remainingTime - Time.deltaTime);
            UpdateTimer();

            if (remainingTime <= 0f)
            {
                EndGame(GetResultByHealth());
            }
        }

        private void ConfigureSplitScreen()
        {
            if (player1Camera != null)
            {
                player1Camera.rect = new Rect(0f, 0f, 0.5f, 1f);
            }

            if (player2Camera != null)
            {
                player2Camera.rect = new Rect(0.5f, 0f, 0.5f, 1f);
            }
        }

        private void UpdateTimer()
        {
            if (timerText == null)
            {
                return;
            }

            int seconds = Mathf.CeilToInt(remainingTime);
            timerText.text = $"{seconds / 60:00}:{seconds % 60:00}";
        }

        private void OnPlayerDefeated(BumperCarHealth defeatedPlayer)
        {
            if (defeatedPlayer == player1Health)
            {
                EndGame("PLAYER 2 WINS");
            }
            else if (defeatedPlayer == player2Health)
            {
                EndGame("PLAYER 1 WINS");
            }
        }

        private string GetResultByHealth()
        {
            float player1 = player1Health == null ? 0f : player1Health.CurrentHealth;
            float player2 = player2Health == null ? 0f : player2Health.CurrentHealth;

            if (Mathf.Approximately(player1, player2))
            {
                return "DRAW";
            }

            return player1 > player2 ? "PLAYER 1 WINS" : "PLAYER 2 WINS";
        }

        private void EndGame(string result)
        {
            if (gameEnded)
            {
                return;
            }

            gameEnded = true;

            if (player1Controller != null)
            {
                player1Controller.SetControlsEnabled(false);
            }

            if (player2Controller != null)
            {
                player2Controller.SetControlsEnabled(false);
            }

            if (resultText != null)
            {
                resultText.text = result;
                resultText.gameObject.SetActive(true);
            }
        }
    }
}
