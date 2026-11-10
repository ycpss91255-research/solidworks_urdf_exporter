cmake_minimum_required(VERSION 2.8.3)

project(TWO_BOXES_PER_CONFIG_description_CFG_RED_ONLY)

find_package(catkin REQUIRED)

catkin_package()

find_package(roslaunch)

foreach(dir config launch meshes urdf)
	install(DIRECTORY ${dir}/
		DESTINATION ${CATKIN_PACKAGE_SHARE_DESTINATION}/${dir})
endforeach(dir)
