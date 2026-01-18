# ArduPong
ArduPong is a modern Pong game for PC, featuring keyboard controls and a custom Arduino joystick built with two analog potentiometers. Developed in C# with an Arduino C++ controller, it showcases real‑time serial communication between hardware and software.

ArduPong
A simple and fun Pong-style game for PC, playable using either the keyboard or a custom Arduino joystick built with two potentiometers.

📌 Project Overview
ArduPong is a modern reinterpretation of the classic Pong game.
The game runs on PC and is developed in C#, while the external controller is built using Arduino (C++) with two analog potentiometers as input devices.

The goal is to provide a hybrid gaming experience: you can play using the keyboard or connect your handmade Arduino joystick for a more physical and engaging control method.

🎮 Features
Classic Pong gameplay

Keyboard controls

Support for a custom Arduino joystick

Serial communication between PC and Arduino

Game logic written in C#

Arduino firmware written in C++

🛠️ Technologies Used
Component	Technology
PC Game	C#
Arduino Controller	C++
Input	2 Potentiometers
Communication	Serial over USB
🔧 Requirements
Windows (or any OS compatible with your C# build)

.NET (specify version if needed)

Arduino Uno/Nano or compatible board

Two potentiometers

USB cable for serial communication

📦 Installation & Setup
PC Side (C# Game)
Clone the repository:

bash
git clone https://github.com/your-username/ArduPong.git
Open the project in Visual Studio or another compatible IDE.

Build and run the game.

Arduino Side (Joystick)
Open the Arduino/ folder in this repository.

Upload the .ino sketch to your Arduino board.

Connect the two potentiometers to the analog pins specified in the code.

Connect the Arduino to your PC via USB.

🎛️ Controls
Keyboard
W / S → Move paddle

Esc → Quit game

Arduino Joystick
Potentiometer 1 → Vertical movement

Potentiometer 2 → (Optional additional function, if implemented)

📡 PC–Arduino Communication
The game reads analog values sent by the Arduino through the serial port.
Make sure the correct COM port is selected in your C# code.

📷 Screenshots
<img width="996" height="604" alt="Screenshot 2026-01-18 180715" src="https://github.com/user-attachments/assets/46e18858-77f5-4896-bd5d-860561007a90" />


<img width="1912" height="1077" alt="Screenshot 2026-01-18 180736" src="https://github.com/user-attachments/assets/fc95c105-b3ae-4e6a-9125-a08381b1e21f" />


🤝 Contributing
Contributions and suggestions are welcome.
Feel free to open an issue or submit a pull request.
