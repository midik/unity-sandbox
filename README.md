## Unity sandbox

[RUS] [[ENG]](#eng)

Это небольшой учебный проект / песочница, где я экспериментирую с Unity на базовом уровне: с объектами, физикой, освещением, текстурами и т.д.

Первая версия представляла собой плоскость, по которой катается шарик определенной массы, сталкиваясь с себе подобными пытаясь соблюдать законы физики. Классика для начинающего.

Сейчас в проекте есть одна сцена, где можно покататься на машинке по бездорожью, убегая от NPC.

Процедурно-генерируемый ландшафт создается генератором на основе мэшей. Он создает прямоугольную область из заданного количества чанков нужного размера и разрешения, согласно заданным параметрам рельефа и свойств. Ландшафт текстурируется и покрывается материалом с некоторыми физическим свойствами. Для большего разнообразия ландшафта используются многослойный генератор шума, Domain Warping, а также поддержка кривой высот. Добавлена поддержка долин (Valleys) двух видов: Noise-based с "притягиванием" к поверхности, и цельных Spline-based долин, которые могут использоваться для размещения дорог или рек.

В игре пока две машины: игрок (управляется WASD) и NPC, который гоняется за игроком по кратчайшему пути и успокаивается, когда догоняет его. Алгоритм NPC самый базовый и нуждается в усовершенствовании.

Машина - это простая текстурированная прямоугольная коробка/коллайдер (Frame), на которой через упругую подвеску подвешены колеса с WheelCollider и CapsuleCollider (чтобы они цеплялись за препятствия). Колесо представляет собой четыре связанных объекта, на каждом из которых своя функция: WheelCollider, скрипт WheelVisualizerAndCollider, MeshRenderer, CapsuleCollider. Передние колеса - поворотные. Реализована имитация разных типов привода (AWD/FWD/RWD), которые можно циклично переключать. Машины (и каждое их колесо) используют RigidBody и имеют массу.

Колеса деформируют ландшафт, оставляя физические следы, согласно заданным параметрам силы деформации и максимальной глубины следа. Учитывается диаметр пятна контакта, масса и скорость вращения колеса. Для реалистичности, точка деформации также смещается вперед пропорционально скорости, т.е. деформация происходит перед колесом. Благодаря ChunkDeformerManager, деформация происходит корректно в том случае, если пятно контакта попадает на несколько соседних чанков. Деформация рассчитывается с помощью Lerp от центра пятна контакта. Колеса пока жесткие (шины не реализованы).

Есть базовый UI, на котором отображается скорость, направление на NPC, лог дейставий NPC, режим камеры (их четыре, от третьего лица). Также в углу экрана в виде PiP рендерится камера от первого лица "из глаз" NPC.

Реализован Respawn. Игрок может респаунить машину по нажатию ESC, а машина NPC респаунится автоматически, через несколько секунд после потери контакта с поверхностью, либо при падении с края карты. Проверка контакта реализована через Raycast по направлению вниз относительно днища машины.

Некоторая часть логики вынесена в базовые классы, например Respawnable и Driveable.

На сцене есть один Directional Light и простой скайбокс. Машины имеют "фары" - пару Spot Light в передней части и небольшим наклоном к поверхности.

На сцене находились несколько десятков тонких прямоугольников, которые можно толкнуть, вызывая эффект домино :) но сейчас они убраны.

---
### Eng

This is a small educational project / sandbox where I am experimenting with Unity at a basic level: with objects, physics, lighting, textures, etc.

The first version was simply a plane where a ball of a certain mass rolled around, colliding with similar ones, trying to obey the laws of physics. A classic beginner's exercise.

Currently, the project has one scene where you can drive a car off-road while running away from an NPC.

A procedurally generated landscape is created by a mesh-based generator. It creates a rectangular area from a specified number of chunks of a given size and resolution, according to specified relief parameters and properties. The landscape is textured and covered with a material having certain physical properties. To add more variety to the landscape, a multi-layered noise generator, Domain Warping, and support for a height curve are used. Support for two types of valleys has been added: Noise-based valleys carving into the surface, and continuous Spline-based valleys, which can be used for placing roads or rivers.

There are currently two vehicles in the game: the player (controlled by WASD) and an NPC that chases the player along the shortest path and stops when it catches them. The NPC algorithm is very basic and needs improvement.

The vehicle is a simple textured rectangular box/collider (Frame), on which wheels with WheelColliders and CapsuleColliders are attached via spring suspension (so they can catch on obstacles). Each wheel consists of four linked objects, each with its own function: WheelCollider, the WheelVisualizerAndCollider script, MeshRenderer, and CapsuleCollider. The front wheels are steerable. Simulation of different drive types (AWD/FWD/RWD) is implemented, which can be cycled through. The vehicles (and each of their wheels) use RigidBody and have mass.

The wheels deform the landscape, leaving physical tracks, according to specified parameters for deformation strength and maximum track depth. The contact patch diameter, wheel mass, and rotation speed are taken into account. For realism, the deformation point is also shifted forward proportionally to the velocity, meaning deformation occurs in front of the wheel. Thanks to the ChunkDeformerManager, deformation is handled correctly when the contact patch overlaps multiple adjacent chunks. Deformation is calculated using Lerp from the center of the contact patch. The wheels are currently rigid (tire simulation is not implemented).

There is a basic UI which displays speed, direction to the NPC, an NPC action log, and the camera mode (four third-person modes). Additionally, a first-person camera view "from the NPC's eyes" is rendered Picture-in-Picture (PiP) in the corner of the screen.

Respawning is implemented. The player can respawn the vehicle by pressing ESC, while the NPC vehicle respawns automatically a few seconds after losing contact with the surface or when falling off the edge of the map. Contact checking is implemented via a Raycast downwards relative to the vehicle's chassis.

Some logic has been extracted into base classes, such as Respawnable and Driveable.

The scene contains one Directional Light and a simple skybox. The vehicles have "headlights" - a pair of Spot Lights at the front, slightly angled towards the ground.

There used to be several dozen thin rectangles on the scene that could be pushed over, causing a domino effect :) but they have now been removed.

## Future plans
- :white_check_mark: NPC
- :white_check_mark: terrain deformation
- :white_check_mark: UI + PiP camera
- :white_check_mark: improve terrain generator
- :white_check_mark: spline valleys
- :black_square_button: add landscape objects
- :black_square_button: add spline-based roads and rivers
- :black_square_button: dynamic chunk loading
- :black_square_button: bigger landscape
- :black_square_button: biomes
- :black_square_button: sounds
- :black_square_button: make NPC smarter
- :black_square_button: add basic gameplay
- :black_square_button: main menu
- :black_square_button: improve car headlights
- :black_square_button: day/night cycle
- :black_square_button: basic weather
- :black_square_button: first person camera
- :black_square_button: ...