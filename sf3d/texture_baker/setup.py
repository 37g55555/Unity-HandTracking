import glob
import os
import platform

import torch
from setuptools import find_packages, setup
from torch.utils.cpp_extension import (
    CUDA_HOME,
    BuildExtension,
    CppExtension,
    CUDAExtension,
)

library_name = "texture_baker"


def get_extensions():
    debug_mode = os.getenv("DEBUG", "0") == "1"
    use_cuda = os.getenv("USE_CUDA", "1" if torch.cuda.is_available() else "0") == "1"
    if debug_mode:
        print("Compiling in debug mode")

    if platform.system() != "Windows":
        raise RuntimeError("texture_baker build is configured for Windows only.")

    use_cuda = use_cuda and CUDA_HOME is not None
    extension = CUDAExtension if use_cuda else CppExtension

    extra_link_args = []
    extra_compile_args = {
        "cxx": [
            "/O2" if not debug_mode else "/Od",
            "/std:c++17",
            "/Zc:preprocessor",
        ],
        "nvcc": [
            "-O3" if not debug_mode else "-O0",
            "-allow-unsupported-compiler",
            "-Xcompiler=/Zc:preprocessor",
        ],
    }
    if debug_mode:
        extra_compile_args["cxx"].append("/Z7")
        extra_compile_args["cxx"].append("/Od")
        extra_link_args.extend(["/DEBUG"])
        extra_compile_args["nvcc"].append("-UNDEBUG")
        extra_compile_args["nvcc"].append("-g")

    define_macros = []
    extensions = []
    libraries = []
    library_dirs = []
    include_dirs = []

    this_dir = os.path.dirname(os.path.curdir)
    sources = glob.glob(
        os.path.join(this_dir, library_name, "csrc", "**", "*.cpp"), recursive=True
    )

    if len(sources) == 0:
        print("No source files found for extension, skipping extension compilation")
        return None

    if use_cuda:
        define_macros += [
            ("THRUST_IGNORE_CUB_VERSION_CHECK", None),
        ]
        sources += glob.glob(
            os.path.join(this_dir, library_name, "csrc", "**", "*.cu"), recursive=True
        )

        cuda_include_root = (
            os.path.join(CUDA_HOME, "Library", "include")
            if os.path.isdir(os.path.join(CUDA_HOME, "Library", "include"))
            else os.path.join(CUDA_HOME, "include")
        )
        libraries += ["cudart", "c10_cuda"]
        include_dirs += [
            cuda_include_root,
            os.path.join(cuda_include_root, "targets", "x64"),
            os.path.join(cuda_include_root, "targets", "x64", "cccl"),
        ]
        library_dirs += [
            os.path.join(CUDA_HOME, "lib", "x64"),
            os.path.join(CUDA_HOME, "lib64"),
            os.path.join(CUDA_HOME, "lib"),
            os.path.join(CUDA_HOME, "libs"),
            os.path.join(CUDA_HOME, "bin"),
        ]

    extensions.append(
        extension(
            name=f"{library_name}._C",
            sources=sources,
            define_macros=define_macros,
            include_dirs=include_dirs,
            extra_compile_args=extra_compile_args,
            extra_link_args=extra_link_args,
            library_dirs=library_dirs,
            libraries=libraries
            + [
                "c10",
                "torch",
                "torch_cpu",
                "torch_python",
            ],
        )
    )

    print(extensions)

    return extensions


setup(
    name=library_name,
    version="0.0.1",
    packages=find_packages(where="."),
    package_dir={"": "."},
    ext_modules=get_extensions(),
    install_requires=[],
    package_data={
        library_name: [os.path.join("csrc", "*.h")],
    },
    description="Small texture baker which rasterizes barycentric coordinates to a tensor.",
    url="https://github.com/Stability-AI/texture_baker",
    cmdclass={"build_ext": BuildExtension},
)
