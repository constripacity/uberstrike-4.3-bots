# Uberstrike 4.3 Bots

## Project Focus
This repository aims to explore how AI-driven bots can integrate into the multiplayer experience of Uberstrike 4.3, creating exciting challenges and engaging scenarios for players. Designed with both developers and enthusiasts in mind, this project showcases practical AI implementations, strategies, and their applications in a real-time video game environment.

## Repository Structure
The repository structure is set up to facilitate easy navigation and development contributions. Key files and directories include:

- `src/` - Contains the core AI logic for the bots
- `assets/` - Game assets used for the demonstration including bot models and textures
- `docs/` - Documentation files, including architecture details and usage guides
- `tests/` - Test cases for bot functionalities

You can find a detailed repository tree [here](docs/PROJECT_TREE.md).

## Getting Started
### Prerequisites
1. Clone this repository using:
```bash
$ git clone https://github.com/constripacity/uberstrike-4.3-bots.git
```
2. Ensure you have Python 3.8 or higher installed.
3. Install dependencies from `requirements.txt` using:
```bash
$ pip install -r requirements.txt
```
4. Follow setup instructions in [`INSTALL.md`](docs/INSTALL.md).

### Running the Bot Demonstration
To run a demonstration, use the following command:
```bash
$ python run_demo.py
```

## AI Bot Features
- **Pathfinding:** AI bots use advanced algorithms like A* for dynamic pathfinding within the game environment.
- **Team Cooperation:** Bots work collaboratively, enabling tactical maneuvers and strategic gameplay.
- **Adaptive Difficulty:** The system adjusts bot difficulty levels in response to player performance.
- **Modular Configuration:** Modify bot behaviors in `config/bot_config.json` to suit different scenarios.

## Disclaimers
- This repository is intended for educational purposes only. It is not affiliated with or endorsed by the original developers of Uberstrike.
- Ensure compliance with the game’s terms and conditions for any external modifications.

---

For any contribution or issue resolution, please feel free to open a pull request or raise an issue.